using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadLogLeakTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Description = "SENTINEL-DESCRIPTION-7782";
    private const string Reference = "SENTINEL-REFERENCE-7782";

    private static readonly Dictionary<string, string?> InformationAndAbove = new()
    {
        ["Serilog:MinimumLevel:Default"] = "Information"
    };

    private readonly CapturingLogSink _sink = new();

    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(
            postgres,
            InformationAndAbove,
            services => services.AddSingleton<ILogEventSink>(_sink));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Reads_AndTheirErrors_LeaveNoSensitiveValueInLogsMetricsOrSpans()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(9876.54m);
        await _world.Host.RegisterAsync(
            accountId,
            "leak-entry",
            EntryType.Debit,
            1357.91m,
            description: Description,
            reference: Reference);
        var unknown = Ledger.Domain.Accounts.AccountId.From(Guid.CreateVersion7()).Value;

        using var durations = new MeterCapture("ledger.balance.query.duration");
        using var commands = new MeterCapture("ledger.db.command.duration");
        using var spans = new ReadSpanCapture();

        using var first = await _world.Client.StatementAsync(accountId, "limit=1");
        var cursor = first.Text("nextCursor");

        using var second = await _world.Client.StatementAsync(accountId, $"limit=1&cursor={Uri.EscapeDataString(cursor)}");
        using var current = await _world.Client.BalanceAsync(accountId);
        using var historical = await _world.Client.BalanceAtAsync(accountId, ReadClock.UtcNow);
        using var badCursor = await _world.Client.StatementAsync(accountId, "cursor=SENTINEL-CURSOR-7782&limit=SENTINEL-LIMIT-7782");
        using var badAsOf = await _world.Client.BalanceAsOfAsync(accountId, "SENTINEL-ASOF-7782");
        using var unknownParameter = await _world.Client.GetAsync($"{ReadApiClient.BalancePath(accountId)}?SENTINEL-NAME-7782=1");
        using var missing = await _world.Client.BalanceAsync(unknown);

        second.Status.ShouldBe(HttpStatusCode.OK, second.Body);
        current.Status.ShouldBe(HttpStatusCode.OK, current.Body);
        historical.Status.ShouldBe(HttpStatusCode.OK, historical.Body);
        badCursor.Status.ShouldBe(HttpStatusCode.BadRequest);
        badAsOf.Status.ShouldBe(HttpStatusCode.BadRequest);
        unknownParameter.Status.ShouldBe(HttpStatusCode.BadRequest);
        missing.Status.ShouldBe(HttpStatusCode.NotFound);

        var logs = _sink.Everything();
        var labels = string.Join(
            ' ',
            durations.Measurements.SelectMany(measurement => measurement.Tags.Values)
                .Concat(commands.Measurements.SelectMany(measurement => measurement.Tags.Values))
                .Concat(spans.Spans.SelectMany(span => span.Tags.Values))
                .Select(value => value?.ToString()));

        logs.ShouldContain("Ledger.Audit");

        foreach (var forbidden in new[]
                 {
                     "SENTINEL", "9876.54", "8518.63", "1357.91", Description, Reference, cursor, "balanceAfter"
                 })
        {
            logs.ShouldNotContain(forbidden, Case.Insensitive);
            labels.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }
}
