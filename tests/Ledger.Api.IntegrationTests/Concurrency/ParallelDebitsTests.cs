using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelDebitsTests(PostgresFixture postgres)
{
    private const int ObserverIntervalMilliseconds = 5;

    public static TheoryData<decimal, decimal, int, int> Scenarios => new()
    {
        { 100.00m, 20.00m, 10, 5 },
        { 500.00m, 10.00m, 100, 50 },
        { 1000.00m, 30.00m, 100, 33 }
    };

    [DockerTheory]
    [MemberData(nameof(Scenarios))]
    public async Task Debits_AgainstABalance_AcceptOnlyWhatFits(decimal balance, decimal amount, int calls, int accepted)
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(balance);

            var results = await ParallelGate.RunAsync(
                calls,
                index => host.RegisterAsync(accountId, $"debit-{index}", EntryType.Debit, amount));

            var entries = await queries.EntriesAsync(accountId);

            results.Count(result => result.IsSuccess).ShouldBe(accepted);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.InsufficientFunds))
                .ShouldBe(calls - accepted);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(balance - (accepted * amount));
            entries.Select(entry => entry.AccountVersion)
                .ShouldBe(Enumerable.Range(1, accepted + 1).Select(version => (long)version));
            entries.Skip(1).Select(entry => entry.BalanceAfter)
                .ShouldBe(Enumerable.Range(1, accepted).Select(step => balance - (step * amount)));
            (await queries.CountKeysAsync(accountId)).ShouldBe(accepted + 1);
            (await queries.CountOutboxAsync(accountId)).ShouldBe(accepted + 1);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task Debits_WithAnOverdraftLimit_StopExactlyAtMinusTheLimit()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(1000.00m, overdraftLimit: 500.00m);

            var results = await ParallelGate.RunAsync(
                100,
                index => host.RegisterAsync(accountId, $"debit-{index}", EntryType.Debit, 30.00m));

            results.Count(result => result.IsSuccess).ShouldBe(50);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(-500.00m);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task ABalanceObserver_NeverSeesTheBalanceBelowMinusTheLimit()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(300.00m, overdraftLimit: 200.00m);
        using var stop = new CancellationTokenSource();

        var observed = new List<decimal>();
        var observer = Task.Run(
            async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    observed.Add((await queries.BalanceRowAsync(accountId)).Balance);

                    await Task.Delay(ObserverIntervalMilliseconds);
                }
            },
            CancellationToken.None);

        await ParallelGate.RunAsync(
            100,
            index => host.RegisterAsync(accountId, $"debit-{index}", EntryType.Debit, 10.00m));

        await stop.CancelAsync();
        await observer;

        observed.ShouldNotBeEmpty();
        observed.Min().ShouldBeGreaterThanOrEqualTo(-200.00m);
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(-200.00m);
    }
}
