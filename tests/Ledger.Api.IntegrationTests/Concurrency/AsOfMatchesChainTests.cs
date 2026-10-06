using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class AsOfMatchesChainTests(PostgresFixture postgres)
{
    private const int Calls = 100;
    private const int Sampled = 50;
    private const decimal OpeningBalance = 100_000.00m;

    [DockerFact]
    public async Task BalanceAsOf_AfterConcurrentWrites_MatchesTheChainEntryByEntry()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var reader = host.Service<IBalanceReader>();
            var accountId = await host.CreateFundedAccountAsync(OpeningBalance);
            var requests = ConcurrencyScenarios.MixedRequests(Calls, ConcurrencyScenarios.RandomSeed);

            var results = await ParallelGate.RunAsync(
                Calls,
                index => host.RegisterAsync(accountId, $"asof-{index}", requests[index].Type, requests[index].Amount));

            results.ShouldAllBe(result => result.IsSuccess);

            var entries = await queries.EntriesAsync(accountId);

            for (var position = 0; position < Sampled; position++)
            {
                var entry = entries[position * (entries.Count / Sampled)];
                var index = entries.ToList().IndexOf(entry);
                var previousBalance = index == 0 ? 0m : entries[index - 1].BalanceAfter;

                var atTheEntry = await reader.ReadAtAsync(
                    accountId,
                    new DateTimeOffset(entry.RecordedAt, TimeSpan.Zero),
                    CancellationToken.None);
                var justBefore = await reader.ReadAtAsync(
                    accountId,
                    new DateTimeOffset(entry.RecordedAt.AddTicks(-10), TimeSpan.Zero),
                    CancellationToken.None);

                atTheEntry.Value.BalanceAfter.ShouldBe(entry.BalanceAfter);
                atTheEntry.Value.LastEntryId.ShouldBe(Ledger.Domain.Entries.EntryId.From(entry.Id).Value);

                if (index == 0)
                {
                    justBefore.Value.BalanceAfter.ShouldBeNull();
                }
                else
                {
                    justBefore.Value.BalanceAfter.ShouldBe(previousBalance);
                }
            }

            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }
}
