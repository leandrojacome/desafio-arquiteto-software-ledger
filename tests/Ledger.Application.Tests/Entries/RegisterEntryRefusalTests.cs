using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using NSubstitute;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class RegisterEntryRefusalTests
{
    private static async Task AssertNothingWrittenAsync(WriteHarness harness)
    {
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(0);
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_NotMatchedAndNoAccountRow_ReturnsAccountNotFound()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(null);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("account_not_found");
    }

    [Fact]
    public async Task HandleAsync_NotMatchedAndDifferentCurrency_ReturnsCurrencyMismatch()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(1000m, currency: "EUR"));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.CurrencyMismatch);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("currency_mismatch");
    }

    [Fact]
    public async Task HandleAsync_NotMatchedAndBalanceTooLow_ReturnsInsufficientFundsWithASingleDiagnosis()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(79.99m));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        await AssertNothingWrittenAsync(harness);
        await harness.Scope.Entries.Received(1).TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Accounts.Received(1)
            .GetForDiagnosisAsync(EntryFixtures.Account, Arg.Any<CancellationToken>());
        harness.Operation.Received(1).Rejected("insufficient_funds");
    }

    [Fact]
    public async Task HandleAsync_NotMatchedButTheAccountNowFits_AppliesASecondTimeAndSucceeds()
    {
        var harness = new WriteHarness();
        var newEntryResult = Result.Success(new AppliedEntry(EntryFixtures.StoredView(), false));
        harness.ApplyReturns(ApplyErrors.NotMatched, newEntryResult);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(500m));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Entry.ShouldBe(newEntryResult.Value.Entry);
        await harness.Scope.Entries.Received(2).TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Accounts.Received(1)
            .GetForDiagnosisAsync(EntryFixtures.Account, Arg.Any<CancellationToken>());
        await harness.Scope.Outbox.Received(1).EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_SecondApplyAlsoNotMatched_ReturnsInsufficientFundsWithoutANewDiagnosis()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched, ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(500m));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        await AssertNothingWrittenAsync(harness);
        await harness.Scope.Entries.Received(2).TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Accounts.Received(1)
            .GetForDiagnosisAsync(EntryFixtures.Account, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SecondApplyFailsWithAnotherError_PassesThatErrorAhead()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched, EntryErrors.AlreadyReversed);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(500m));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
    }

    [Fact]
    public async Task HandleAsync_RefusedDebit_DoesNotConsumeTheKeyBecauseTheTransactionIsRolledBack()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        harness.UnitOfWork.Commits.ShouldBe(0);
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_CreditNotMatchedBecauseOfCurrency_ReturnsCurrencyMismatch()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(0m, currency: "EUR"));

        var result = await harness.Register.HandleAsync(
            EntryFixtures.RegisterCommand(EntryType.Credit),
            CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.CurrencyMismatch);
    }

    [Theory]
    [InlineData("0.00", "100.00", "100.00", true)]
    [InlineData("0.00", "100.01", "100.00", false)]
    [InlineData("500.00", "600.00", "100.00", true)]
    [InlineData("500.00", "600.01", "100.00", false)]
    public async Task HandleAsync_RefusalClassification_FollowsTheDatabaseRuleWithOverdraft(
        string overdraft,
        string debit,
        string balance,
        bool accountNowFits)
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched, ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(Decimal(balance), Decimal(overdraft)));

        var result = await harness.Register.HandleAsync(
            EntryFixtures.RegisterCommand(amount: Decimal(debit)),
            CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        harness.Scope.Entries.ReceivedCalls().Count().ShouldBe(accountNowFits ? 2 : 1);
    }

    private static decimal Decimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);
}
