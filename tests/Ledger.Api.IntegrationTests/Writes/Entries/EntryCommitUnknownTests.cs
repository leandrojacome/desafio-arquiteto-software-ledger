using System.Globalization;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class EntryCommitUnknownTests(PostgresFixture postgres)
{
    private readonly WriteTestData _data = new(postgres);

    private static Dictionary<string, string?> ThroughTheProxy(CommitCuttingProxy proxy, int retries)
    {
        return new Dictionary<string, string?>
        {
            ["Postgres:Host"] = "127.0.0.1",
            ["Postgres:Port"] = proxy.Port.ToString(CultureInfo.InvariantCulture),
            ["Resilience:Retry:MaxRetryAttempts"] = retries.ToString(CultureInfo.InvariantCulture)
        };
    }

    [DockerFact]
    public async Task LostAnswerOfTheCommit_IsRecoveredInsideTheRequestWithoutDuplicatingTheEntry()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var factory = new WriteApiFactory(postgres, ThroughTheProxy(proxy, 2));
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");

        proxy.CutNextCommit();

        var response = await client.DebitAsync(accountId, "10.00");

        proxy.CommitsCut.ShouldBe(1);
        response.StatusCode.ShouldBe(201, response.Body);
        response.Text("balanceAfter").ShouldBe("90.00");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(90.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task LostAnswerOfTheCommitWithoutRetries_Is503AndRepeatingTheCallReturnsTheCommittedEntry()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var factory = new WriteApiFactory(postgres, ThroughTheProxy(proxy, 0));
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var key = WriteClient.NewKey();

        proxy.CutNextCommit();

        var lost = await client.DebitAsync(accountId, "10.00", key);

        lost.ShouldBeProblem(503, "SERVICE_UNAVAILABLE");
        lost.Header("Retry-After").ShouldBe("1");

        var repeated = await client.DebitAsync(accountId, "10.00", key);

        repeated.StatusCode.ShouldBe(201, repeated.Body);
        repeated.Header("Idempotent-Replayed").ShouldBe("true");
        repeated.Text("balanceAfter").ShouldBe("90.00");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(2);
        (await _data.BalanceAsync(accountId)).ShouldBe(90.00m);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task LostAnswerOfTheCommitOnAccountCreation_StillCreatesExactlyOneAccountAndOneAuditRow()
    {
        await using var proxy = CommitCuttingProxy.Start(postgres.Settings.Host, postgres.Settings.Port);
        await using var factory = new WriteApiFactory(postgres, ThroughTheProxy(proxy, 2));
        var client = new WriteClient(factory.CreateClient());
        var accountsBefore = await _data.CountAccountsAsync();

        proxy.CutNextCommit();

        var response = await client.PostAccountAsync();

        proxy.CommitsCut.ShouldBe(1);
        response.StatusCode.ShouldBe(201, response.Body);
        (await _data.CountAccountsAsync()).ShouldBe(accountsBefore + 1);
        (await _data.AuditRowsAsync(response.Text("accountId"), "account.created")).ShouldHaveSingleItem();
        (await _data.StoredAccountAsync(response.Text("accountId"))).Balance.ShouldBe(0m);
    }
}
