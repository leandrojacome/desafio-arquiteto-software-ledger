using System.Net;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Reads;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed partial class CurrentBalanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ReadWorld _world = null!;

    public Task InitializeAsync()
    {
        _world = ReadWorld.Create(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _world.DisposeAsync();
    }

    [DockerFact]
    public async Task Balance_AfterACreditAndADebit_ReturnsTheCurrentStateOfTheAccount()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(1000.00m);
        var debit = await _world.Host.RegisterAsync(accountId, "debit-150", EntryType.Debit, 150.00m);

        using var response = await _world.Client.BalanceAsync(accountId);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.ContentType.ShouldBe("application/json");
        response.Text("accountId").ShouldBe(accountId.ToString());
        response.Text("currency").ShouldBe("BRL");
        response.Text("balance").ShouldBe("850.00");
        response.Text("overdraftLimit").ShouldBe("0.00");
        response.Text("lastEntryId").ShouldBe(debit.Value.Entry.Id.ToString());
        InstantShape().IsMatch(response.Text("asOf")).ShouldBeTrue(response.Body);
        response.Has("settled").ShouldBeFalse();
    }

    [DockerFact]
    public async Task Balance_OfAnAccountWithoutEntries_IsZeroWithoutALastEntry()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.BalanceAsync(accountId);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text("balance").ShouldBe("0.00");
        response.IsNull("lastEntryId").ShouldBeTrue();
        response.Has("lastEntryId").ShouldBeTrue();
    }

    [DockerFact]
    public async Task Balance_InsideTheOverdraftLimit_IsNegativeAndKeepsTheSign()
    {
        var accountId = await _world.Host.CreateAccountAsync(overdraftLimit: 50.00m);
        await _world.Host.RegisterAsync(accountId, "overdraw", EntryType.Debit, 50.00m);

        using var response = await _world.Client.BalanceAsync(accountId);

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Text("balance").ShouldBe("-50.00");
        response.Text("overdraftLimit").ShouldBe("50.00");
    }

    [DockerFact]
    public async Task Balance_ConsultedThreeTimes_RepeatsTheBalanceAndAdvancesTheInstant()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(300.00m);
        var balances = new List<string>();
        var lastEntries = new List<string>();
        var instants = new List<string>();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await _world.Client.BalanceAsync(accountId);

            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            balances.Add(response.Text("balance"));
            lastEntries.Add(response.Text("lastEntryId"));
            instants.Add(response.Text("asOf"));
        }

        balances.Distinct().ShouldHaveSingleItem().ShouldBe("300.00");
        lastEntries.Distinct().Count().ShouldBe(1);
        instants.Distinct().Count().ShouldBe(3);
        instants.ShouldBe([.. instants.Order(StringComparer.Ordinal)]);
    }

    [DockerFact]
    public async Task Balance_EchoesTheCorrelationIdAndForbidsCaching()
    {
        var accountId = await _world.Host.CreateAccountAsync();

        using var response = await _world.Client.BalanceAsync(accountId, "abc-12345");

        response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
        response.Header("X-Correlation-Id").ShouldBe("abc-12345");
        response.Header("Cache-Control").ShouldBe("no-store");
        response.HasHeader("ETag").ShouldBeFalse();
        response.HasHeader("Last-Modified").ShouldBeFalse();
    }

    [DockerFact]
    public async Task Balance_OfAnUnknownAccount_Returns404AccountNotFound()
    {
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        using var response = await _world.Client.BalanceAsync(unknown);

        response.ShouldBeProblem(
            HttpStatusCode.NotFound,
            "ACCOUNT_NOT_FOUND",
            "Conta não encontrada",
            ReadApiClient.BalancePath(unknown));
        response.Header("Cache-Control").ShouldBe("no-store");
        response.ShouldCarryNoInternals();
    }

    [DockerFact]
    public async Task Balance_AfterEveryWriteOfASequence_ReflectsTheLastCommittedEntry()
    {
        var accountId = await _world.Host.CreateFundedAccountAsync(100.00m);

        for (var step = 1; step <= 25; step++)
        {
            var type = step % 3 == 0 ? EntryType.Debit : EntryType.Credit;
            var written = await _world.Host.RegisterAsync(accountId, $"sequence-{step}", type, 7.50m);

            using var response = await _world.Client.BalanceAsync(accountId);

            written.IsSuccess.ShouldBeTrue();
            response.Status.ShouldBe(HttpStatusCode.OK, response.Body);
            response.Money("balance").ShouldBe(written.Value.Entry.BalanceAfter.Amount);
            response.Text("lastEntryId").ShouldBe(written.Value.Entry.Id.ToString());
        }
    }

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{6}Z\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstantShape();
}
