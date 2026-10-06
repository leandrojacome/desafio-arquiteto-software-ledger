using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelEntriesPreserveSumTests(PostgresFixture postgres)
{
    private const decimal OpeningBalance = 100_000.00m;

    [DockerTheory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    public async Task ParallelMixedEntries_OnOneAccount_PreserveTheSumAndTheChain(int calls)
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(OpeningBalance);
            var requests = ConcurrencyScenarios.MixedRequests(calls, ConcurrencyScenarios.RandomSeed + calls);

            var results = await ParallelGate.RunAsync(
                calls,
                index => host.RegisterAsync(accountId, $"mixed-{index}", requests[index].Type, requests[index].Amount));

            var entries = await queries.EntriesAsync(accountId);

            results.ShouldAllBe(result => result.IsSuccess);
            entries.Count.ShouldBe(calls + 1);
            entries.Select(entry => entry.AccountVersion)
                .ShouldBe(Enumerable.Range(1, calls + 1).Select(version => (long)version));
            (await queries.BalanceRowAsync(accountId)).Balance
                .ShouldBe(OpeningBalance + ConcurrencyScenarios.SignedSum(requests));
            (await queries.CountOutboxAsync(accountId)).ShouldBe(calls + 1);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }
}
