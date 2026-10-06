using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ManyAccountsTests(PostgresFixture postgres)
{
    private const decimal OpeningBalance = 20_000.00m;

    [DockerTheory]
    [InlineData(20, 10)]
    [InlineData(50, 20)]
    public async Task ParallelEntries_AcrossManyAccounts_KeepEveryAccountConsistent(int accounts, int entriesPerAccount)
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountIds = new List<AccountId>();

            for (var account = 0; account < accounts; account++)
            {
                accountIds.Add(await host.CreateFundedAccountAsync(OpeningBalance));
            }

            var requests = ConcurrencyScenarios.MixedRequests(entriesPerAccount, ConcurrencyScenarios.RandomSeed);

            var results = await ParallelGate.RunAsync(
                accounts * entriesPerAccount,
                index =>
                {
                    var request = requests[index % entriesPerAccount];

                    return host.RegisterAsync(
                        accountIds[index / entriesPerAccount],
                        $"many-{index}",
                        request.Type,
                        request.Amount);
                });

            results.ShouldAllBe(result => result.IsSuccess);

            foreach (var accountId in accountIds)
            {
                (await queries.CountEntriesAsync(accountId)).ShouldBe(entriesPerAccount + 1);
                (await queries.BalanceRowAsync(accountId)).Balance
                    .ShouldBe(OpeningBalance + ConcurrencyScenarios.SignedSum(requests));
                await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
            }
        });
    }
}
