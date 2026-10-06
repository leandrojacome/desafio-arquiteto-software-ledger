using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Entries;

namespace Ledger.Api.IntegrationTests.Concurrency;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class ParallelReversalsTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task ParallelReversals_OfTheSameDebit_AllowOnlyOne()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateFundedAccountAsync(100.00m);
            var debit = await host.RegisterAsync(accountId, "to-be-reversed", EntryType.Debit, 40.00m);

            var results = await ParallelGate.RunAsync(
                20,
                index => host.ReverseAsync(accountId, debit.Value.Entry.Id, $"reversal-{index}"));

            results.Count(result => result.IsSuccess).ShouldBe(1);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.AlreadyReversed)).ShouldBe(19);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(100.00m);
            (await queries.CountEntriesAsync(accountId)).ShouldBe(3);
            (await queries.CountOutboxAsync(accountId)).ShouldBe(3);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task ParallelReversals_OfACreditThatIsTheWholeBalance_AnswerAlreadyReversedAndNeverInsufficientFunds()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var queries = new LedgerQueries(postgres);
            var accountId = await host.CreateAccountAsync();
            var credit = await host.RegisterAsync(accountId, "whole-balance", EntryType.Credit, 80.00m);

            var results = await ParallelGate.RunAsync(
                20,
                index => host.ReverseAsync(accountId, credit.Value.Entry.Id, $"reversal-{index}"));

            results.Count(result => result.IsSuccess).ShouldBe(1);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.AlreadyReversed)).ShouldBe(19);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.InsufficientFunds)).ShouldBe(0);
            (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(0m);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task CompetingReversalsOfACredit_WhenTheBalanceCoversOnlyOne_GiveOneCreatedAndOneAlreadyReversed()
    {
        await using var host = LedgerHost.Create(postgres, LedgerHost.WideWritePool);

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var accountId = await host.CreateAccountAsync();
            var credit = await host.RegisterAsync(accountId, "covers-one", EntryType.Credit, 80.00m);
            await host.RegisterAsync(accountId, "small-top-up", EntryType.Credit, 10.00m);

            var results = await ParallelGate.RunAsync(
                2,
                index => host.ReverseAsync(accountId, credit.Value.Entry.Id, $"reversal-{index}"));

            results.Count(result => result.IsSuccess).ShouldBe(1);
            results.Count(result => ConcurrencyScenarios.Refused(result, EntryErrors.AlreadyReversed)).ShouldBe(1);
            await ConcurrencyScenarios.AssertConsistentAsync(postgres, accountId);
        });
    }

    [DockerFact]
    public async Task ReversalOfACredit_WhenTheMoneyWasSpent_IsInsufficientFundsAndLeavesTheOriginalReversible()
    {
        await using var host = LedgerHost.Create(postgres);
        var queries = new LedgerQueries(postgres);
        var accountId = await host.CreateAccountAsync();
        var credit = await host.RegisterAsync(accountId, "spent", EntryType.Credit, 80.00m);
        await host.RegisterAsync(accountId, "spend", EntryType.Debit, 70.00m);

        var refused = await host.ReverseAsync(accountId, credit.Value.Entry.Id, "reversal-attempt");

        ConcurrencyScenarios.Refused(refused, EntryErrors.InsufficientFunds).ShouldBeTrue();

        await host.RegisterAsync(accountId, "top-up", EntryType.Credit, 100.00m);
        var accepted = await host.ReverseAsync(accountId, credit.Value.Entry.Id, "reversal-attempt");

        accepted.IsSuccess.ShouldBeTrue();
        accepted.Value.IsReplay.ShouldBeFalse();
        (await queries.BalanceRowAsync(accountId)).Balance.ShouldBe(30.00m);
    }

    [DockerFact]
    public async Task Reversal_OfAnEntryOfAnotherAccount_IsNotFound()
    {
        await using var host = LedgerHost.Create(postgres);
        var owner = await host.CreateFundedAccountAsync(100.00m);
        var stranger = await host.CreateAccountAsync();
        var debit = await host.RegisterAsync(owner, "owned", EntryType.Debit, 10.00m);

        var result = await host.ReverseAsync(stranger, debit.Value.Entry.Id, "stranger");

        ConcurrencyScenarios.Refused(result, EntryErrors.NotFound).ShouldBeTrue();
    }
}
