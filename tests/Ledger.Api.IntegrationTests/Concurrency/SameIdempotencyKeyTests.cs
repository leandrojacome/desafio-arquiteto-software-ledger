using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class SameIdempotencyKeyTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task SameIdempotencyKey_InParallel_CreatesASingleEntry()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(100.00m);

            var results = await ParallelGate.RunAsync(
                40,
                _ => host.RegisterAsync(accountId, "same-key", EntryType.Debit, 10.00m));

            results.ShouldAllBe(result => result.IsSuccess);
            results.Count(result => !result.Value.IsReplay).ShouldBe(1);
            results.Count(result => result.Value.IsReplay).ShouldBe(39);
            results.Select(result => result.Value.Entry.Id).Distinct().Count().ShouldBe(1);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(90.00m);
            (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
            (await queries.CountOutboxAsync(accountId)).ShouldBe(2);
            (await queries.CountKeysAsync(accountId)).ShouldBe(2);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task SameIdempotencyKeyWithDifferentBodies_InParallel_AllowsExactlyOne()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(1_000.00m);

            var results = await ParallelGate.RunAsync(
                20,
                index => host.RegisterAsync(accountId, "contested-key", EntryType.Debit, index + 1));

            var winnerIndex = results.ToList().FindIndex(result => result.IsSuccess);

            results.Count(result => result.IsSuccess).ShouldBe(1);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.IdempotencyKeyReused)).ShouldBe(19);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(1_000.00m - (winnerIndex + 1));
            (await queries.CountEntriesAsync(accountId)).ShouldBe(2);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task SameIdempotencyKey_InTwoAccounts_CreatesOneEntryInEach()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var queries = new LedgerQueries(postgres);
        var first = await host.CreateFundedAccountAsync(100.00m);
        var second = await host.CreateFundedAccountAsync(100.00m);

        var results = await ParallelGate.RunAsync(
            2,
            index => host.RegisterAsync(index == 0 ? first : second, "shared-key", EntryType.Debit, 10.00m));

        results.ShouldAllBe(result => result.IsSuccess && !result.Value.IsReplay);
        (await queries.BalanceRowAsync(first)).Balance.ShouldBe(90.00m);
        (await queries.BalanceRowAsync(second)).Balance.ShouldBe(90.00m);
    }

    [DockerFact]
    public async Task SameKeyFromAnotherClient_WithTheSameBody_IsAConflictAndNeverAReplay()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);
        var accountId = await host.CreateFundedAccountAsync(100.00m);

        var first = await host.RegisterAsync(accountId, "client-key", EntryType.Debit, 10.00m, clientId: "pix-core");
        var other = await host.RegisterAsync(accountId, "client-key", EntryType.Debit, 10.00m, clientId: "cards");

        first.IsSuccess.ShouldBeTrue();
        ConcurrencyScenarios.Refused(other, EntryErrors.IdempotencyKeyReused).ShouldBeTrue();
    }

    [DockerFact]
    public async Task RefusedDebit_DoesNotConsumeTheIdempotencyKey()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateFundedAccountAsync(10.00m);

        var refused = await host.RegisterAsync(accountId, "retried-key", EntryType.Debit, 50.00m);
        var keysAfterTheRefusal = await queries.CountKeysAsync(accountId);
        await host.RegisterAsync(accountId, "top-up", EntryType.Credit, 100.00m);
        var accepted = await host.RegisterAsync(accountId, "retried-key", EntryType.Debit, 50.00m);

        ConcurrencyScenarios.Refused(refused, EntryErrors.InsufficientFunds).ShouldBeTrue();
        keysAfterTheRefusal.ShouldBe(1);
        accepted.IsSuccess.ShouldBeTrue();
        accepted.Value.IsReplay.ShouldBeFalse();
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(60.00m);
    }
}
