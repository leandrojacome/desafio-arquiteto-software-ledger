using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadAuditLogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ClientId = "audit-client";
    private const string Description = "SENTINEL-DESCRIPTION-4417";
    private const string Reference = "SENTINEL-REFERENCE-4417";

    private static readonly Dictionary<string, string?> OnlyWarnings = new()
    {
        ["Serilog:MinimumLevel:Default"] = "Warning"
    };

    private readonly CapturingLogSink _sink = new();

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(
            postgres,
            OnlyWarnings,
            services => services.AddSingleton<ILogEventSink>(_sink),
            TestTokenFactory.Create("ledger.read", ClientId));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Reads_OnSuccess_EmitOneAuditEventEachEvenWhenTheDefaultLevelIsWarning()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(100.00m);
        var asOf = ReadClock.UtcNow.AddMinutes(-10);

        using var current = await _world.Client.BalanceAsync(accountId, "audit-correlation-0001");
        using var historical = await _world.Client.BalanceAtAsync(accountId, asOf);
        using var statement = await _world.Client.StatementAsync(
            accountId,
            "limit=5&from=2020-01-01T00:00:00Z&to=2099-01-01T00:00:00Z",
            "audit-correlation-0003");

        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        statement.Status.ShouldBe(HttpStatusCode.OK, statement.Body);

        var audit = _sink.Events.Where(LogEventReading.IsAudit).ToList();

        audit.Select(LogEventReading.EventId).ShouldBe([2001, 2001, 2002]);
        audit.ShouldAllBe(logEvent => logEvent.Level == LogEventLevel.Information);

        var first = audit[0];
        var second = audit[1];
        var third = audit[2];

        LogEventReading.Text(first, "ClientId").ShouldBe(ClientId);
        LogEventReading.Text(first, "AccountId").ShouldBe(accountId.ToString());
        LogEventReading.Text(first, "CorrelationId").ShouldBe("audit-correlation-0001");
        LogEventReading.Text(first, "Mode").ShouldBe("CURRENT");
        LogEventReading.Text(second, "Mode").ShouldBe("AS_OF");
        LogEventReading.Text(second, "AsOf").ShouldNotBeNullOrWhiteSpace();
        LogEventReading.Text(third, "ClientId").ShouldBe(ClientId);
        LogEventReading.Text(third, "AccountId").ShouldBe(accountId.ToString());
        LogEventReading.Text(third, "CorrelationId").ShouldBe("audit-correlation-0003");
        LogEventReading.Text(third, "Limit").ShouldBe("5");
        LogEventReading.Text(third, "Returned").ShouldBe("1");
        LogEventReading.Text(third, "HasCursor").ShouldBe("False");
        LogEventReading.Has(third, "From").ShouldBeTrue();
        LogEventReading.Has(third, "To").ShouldBeTrue();
    }

    [DockerFact]
    public async Task Statement_WithACursor_RecordsThatItHadOneButNeverTheCursor()
    {
        var accountId = await _world.Host.CreateAccountAsync();
        await _world.Ledger.SeedChainAsync(accountId, 5, ReadClock.UtcNow.AddHours(-2), TimeSpan.FromSeconds(1));

        using var first = await _world.Client.StatementAsync(accountId, "limit=2");
        var cursor = first.Text("nextCursor");
        using var second = await _world.Client.StatementAsync(accountId, $"limit=2&cursor={Uri.EscapeDataString(cursor)}");

        var audit = _sink.Events.Where(LogEventReading.IsAudit).ToList();

        audit.Count.ShouldBe(2);
        LogEventReading.Text(audit[0], "HasCursor").ShouldBe("False");
        LogEventReading.Text(audit[1], "HasCursor").ShouldBe("True");
        _sink.Everything().ShouldNotContain(cursor);
    }

    [DockerFact]
    public async Task Reads_ThatDoNotSucceed_EmitNoAuditEvent()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(10.00m);
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;
        using var anonymous = ReadApiClient.Anonymous(_world.Factory);
        using var writeOnly = ReadApiClient.For(_world.Factory, TestTokenFactory.Create("ledger.write"));

        using var badAsOf = await _world.Client.BalanceAsOfAsync(accountId, "abc");
        using var badQuery = await _world.Client.StatementAsync(accountId, "limit=0");
        using var missingBalance = await _world.Client.BalanceAsync(unknown);
        using var missingStatement = await _world.Client.StatementAsync(unknown);
        using var future = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow.AddDays(1));
        using var noToken = await anonymous.BalanceAsync(accountId);
        using var wrongScope = await writeOnly.BalanceAsync(accountId);

        badAsOf.Status.ShouldBe(HttpStatusCode.BadRequest);
        badQuery.Status.ShouldBe(HttpStatusCode.BadRequest);
        missingBalance.Status.ShouldBe(HttpStatusCode.NotFound);
        missingStatement.Status.ShouldBe(HttpStatusCode.NotFound);
        future.Status.ShouldBe(HttpStatusCode.BadRequest);
        noToken.Status.ShouldBe(HttpStatusCode.Unauthorized);
        wrongScope.Status.ShouldBe(HttpStatusCode.Forbidden);
        _sink.Events.Where(LogEventReading.IsAudit).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task AuditEvents_NeverCarryBalancesAmountsDescriptionsOrReferences()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(7777.77m);
        await _world.Host.RegisterAsync(
            accountId,
            "sentinel-entry",
            EntryType.Debit,
            1234.56m,
            description: Description,
            reference: Reference);

        using var current = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow);
        using var statement = await _world.Client.StatementAsync(accountId);

        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        statement.Body.ShouldContain(Description);

        var everything = string.Join(
            Environment.NewLine,
            _sink.Events.Where(LogEventReading.IsAudit).Select(CapturingLogSink.Json));

        _sink.Events.Count(LogEventReading.IsAudit).ShouldBe(3);

        foreach (var forbidden in new[] { "7777.77", "6543.21", "1234.56", Description, Reference, "balanceAfter", "\"amount\"" })
        {
            everything.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }
}
