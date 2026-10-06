using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelDebitsHttpTests(PostgresFixture postgres)
{
    private const int ObserverIntervalMilliseconds = 5;

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task Debits_AgainstABalance_AcceptOnlyWhatFitsAndStepTheChainDown()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("100.00");

            var responses = await ParallelGate.RunAsync(10, _ => client.DebitAsync(accountId, "20.00"));

            responses.Count(response => response.StatusCode == 201).ShouldBe(5);
            responses.Where(response => response.StatusCode != 201).ShouldAllBe(response => response.StatusCode == 422);
            responses.Where(response => response.StatusCode == 422)
                .ShouldAllBe(response => response.Text("code") == "INSUFFICIENT_FUNDS");
            responses.Where(response => response.StatusCode == 201)
                .Select(response => response.Text("balanceAfter"))
                .Order()
                .ShouldBe(["0.00", "20.00", "40.00", "60.00", "80.00"]);
            (await _data.BalanceAsync(accountId)).ShouldBe(0m);
            (await _data.CountEntriesAsync(accountId)).ShouldBe(6);
            (await _data.CountOutboxAsync(accountId)).ShouldBe(6);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task Debits_ThatDrainTheBalance_LeaveZeroAndNeverShowItNegative()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("500.00");

            using var observing = new CancellationTokenSource();
            var lowest = decimal.MaxValue;
            var observer = Task.Run(
                async () =>
                {
                    while (!observing.IsCancellationRequested)
                    {
                        lowest = Math.Min(lowest, await _data.BalanceAsync(accountId));
                        await Task.Delay(ObserverIntervalMilliseconds);
                    }
                },
                CancellationToken.None);

            var responses = await ParallelGate.RunAsync(100, _ => client.DebitAsync(accountId, "10.00"));

            await observing.CancelAsync();
            await observer;

            lowest.ShouldBeGreaterThanOrEqualTo(0m);
            responses.Count(response => response.StatusCode == 201).ShouldBe(50);
            responses.Count(response => response.StatusCode == 422).ShouldBe(50);
            responses.Where(response => response.StatusCode == 422)
                .ShouldAllBe(response => response.Text("code") == "INSUFFICIENT_FUNDS");
            responses.Where(response => response.StatusCode == 201)
                .Select(response => response.Json().GetProperty("accountVersion").GetInt64())
                .Order()
                .ShouldBe(Enumerable.Range(2, 50).Select(version => (long)version));
            (await _data.BalanceAsync(accountId)).ShouldBe(0m);
            (await _data.CountEntriesAsync(accountId)).ShouldBe(51);
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task DebitsAndCreditsTogether_KeepTheSumAndEveryAnswerIsCreated()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await client.CreateFundedAccountAsync("1000.00");

            var responses = await ParallelGate.RunAsync(
                64,
                index => index % 2 == 0
                    ? client.CreditAsync(accountId, "5.00")
                    : client.DebitAsync(accountId, "3.00"));

            responses.ShouldAllBe(response => response.StatusCode == 201);
            (await _data.BalanceAsync(accountId)).ShouldBe(1000.00m + (32 * 5.00m) - (32 * 3.00m));
            await _data.AssertConsistentAsync(accountId);
        });
    }

    [DockerFact]
    public async Task ManyAccountsAtOnce_EachKeepsItsOwnChain()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var accounts = new List<string>();

        for (var index = 0; index < 10; index++)
        {
            accounts.Add(await client.CreateFundedAccountAsync("100.00"));
        }

        var responses = await ParallelGate.RunAsync(
            accounts.Count * 8,
            index => client.DebitAsync(accounts[index % accounts.Count], "10.00"));

        responses.ShouldAllBe(response => response.StatusCode == 201);

        foreach (var accountId in accounts)
        {
            (await _data.BalanceAsync(accountId)).ShouldBe(20.00m);
            await _data.AssertConsistentAsync(accountId);
        }
    }

    [DockerFact]
    public async Task WithTheDefaultPool_EveryAnswerIsCreatedRefusedOrRetryableAndTheBooksStayExact()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("500.00");

        var responses = await ParallelGate.RunAsync(100, _ => client.DebitAsync(accountId, "10.00"));

        responses.Select(response => response.StatusCode).ShouldAllBe(status => status == 201 || status == 422 || status == 503);
        responses.Where(response => response.StatusCode == 503)
            .ShouldAllBe(response => response.Header("Retry-After") == "1" && response.Text("code") == "SERVICE_UNAVAILABLE");
        responses.Where(response => response.StatusCode == 422)
            .ShouldAllBe(response => response.Text("code") == "INSUFFICIENT_FUNDS");

        var accepted = responses.Count(response => response.StatusCode == 201);

        (await _data.BalanceAsync(accountId)).ShouldBe(500.00m - (accepted * 10.00m));
        (await _data.CountEntriesAsync(accountId)).ShouldBe(accepted + 1);
        (await _data.CountKeysAsync(accountId)).ShouldBe(accepted + 1);
        (await _data.CountOutboxAsync(accountId)).ShouldBe(accepted + 1);
        await _data.AssertConsistentAsync(accountId);
    }

    [DockerFact]
    public async Task ManyAccountsCreatedAtOnce_GetDistinctIdentifiers()
    {
        await using var factory = new WriteApiFactory(postgres, WriteApiFactory.WidePool);
        var client = new WriteClient(factory.CreateClient());
        var before = await _data.CountAccountsAsync();

        var responses = await ParallelGate.RunAsync(50, _ => client.PostAccountAsync());

        responses.ShouldAllBe(response => response.StatusCode == 201);
        responses.Select(response => response.Text("accountId")).Distinct().Count().ShouldBe(50);
        (await _data.CountAccountsAsync()).ShouldBe(before + 50);
    }
}
