using System.Text.Json;
using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class ReverseEntryHandlerTests
{
    private static WriteHarness HarnessWithOriginal(ReversalCandidate? candidate = null)
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(candidate ?? EntryFixtures.Candidate());

        return harness;
    }

    private static NewEntry AppliedNewEntry(WriteHarness harness) =>
        harness.Scope.Entries.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<NewEntry>()
            .Single();

    private static async Task AssertNothingWrittenAsync(WriteHarness harness)
    {
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(0);
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_DebitOriginal_AppliesACreditWithTheSameAmountPointingAtTheOriginal()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(EntryType.Debit, 150.00m));
        var command = EntryFixtures.ReverseCommand(description: "  Cobrança duplicada  ");

        var result = await harness.Reverse.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeFalse();
        var applied = AppliedNewEntry(harness);
        applied.Entry.Type.ShouldBe(EntryType.Credit);
        applied.Entry.Amount.ShouldBe(EntryFixtures.Brl(150.00m));
        applied.Entry.ReversesEntryId.ShouldBe(EntryFixtures.OriginalEntry);
        applied.Entry.OccurredAt.ShouldBeNull();
        applied.Entry.Reference.ShouldBeNull();
        applied.Entry.Description.ShouldBe("Cobrança duplicada");
        applied.Entry.Id.ShouldBe(EntryFixtures.Entry);
        applied.ClientId.ShouldBe(EntryFixtures.ClientId);
        applied.CorrelationId.ShouldBe(EntryFixtures.CorrelationId);
    }

    [Fact]
    public async Task HandleAsync_CreditOriginal_AppliesADebit()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(EntryType.Credit, 300.00m));

        await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        var applied = AppliedNewEntry(harness);
        applied.Entry.Type.ShouldBe(EntryType.Debit);
        applied.Entry.Amount.ShouldBe(EntryFixtures.Brl(300.00m));
    }

    [Fact]
    public async Task HandleAsync_ValidReversal_ReservesReadsTheOriginalAppliesAndEnqueuesInOrder()
    {
        var harness = HarnessWithOriginal();
        var command = EntryFixtures.ReverseCommand();
        var expectedHash = CanonicalRequestHash.ForReversal(command);

        await harness.Reverse.HandleAsync(command, CancellationToken.None);

        Received.InOrder(() =>
        {
            harness.Scope.IdempotencyKeys.TryReserveAsync(
                EntryFixtures.Account,
                EntryFixtures.Key,
                Arg.Is<ReadOnlyMemory<byte>>(hash => hash.ToArray().SequenceEqual(expectedHash)),
                CanonicalRequestHash.CurrentVersion,
                EntryFixtures.Entry,
                Arg.Any<CancellationToken>());
            harness.Scope.Entries.FindForReversalAsync(
                EntryFixtures.Account,
                EntryFixtures.OriginalEntry,
                Arg.Any<CancellationToken>());
            harness.Scope.Entries.TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
            harness.Scope.Outbox.EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        });
        harness.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_ValidReversal_WritesTheEventWithReversesEntryId()
    {
        var harness = HarnessWithOriginal();

        await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        var message = harness.Scope.Outbox.ReceivedCalls().Single().GetArguments().OfType<OutboxMessage>().Single();
        message.Type.ShouldBe("EntryRegistered");
        message.Id.ShouldBe(EntryFixtures.EventGuid);
        message.TraceParent.ShouldBe(EntryFixtures.TraceParent);
        using var document = JsonDocument.Parse(message.Payload);
        document.RootElement.GetProperty("reversesEntryId").GetString().ShouldBe(EntryFixtures.OriginalEntry.ToString());
        document.RootElement.GetProperty("eventId").GetString().ShouldBe(EntryFixtures.EventGuid.ToString());
    }

    [Fact]
    public async Task HandleAsync_OriginalNotFound_ReturnsEntryNotFoundAndWritesNothing()
    {
        var harness = new WriteHarness();
        harness.OriginalReturns(null);

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.NotFound);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("entry_not_found");
    }

    [Fact]
    public async Task HandleAsync_OriginalIsAReversal_ReturnsNotReversible()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(reversesEntryId: EntryFixtures.Entry));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.NotReversible);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("entry_not_reversible");
    }

    [Fact]
    public async Task HandleAsync_OriginalAlreadyReversed_ReturnsAlreadyReversed()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(reversalId: EntryFixtures.Entry));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("entry_already_reversed");
    }

    [Fact]
    public async Task HandleAsync_OriginalIsAReversalAndAlsoReversed_ReportsNotReversibleFirst()
    {
        var harness = HarnessWithOriginal(
            EntryFixtures.Candidate(reversesEntryId: EntryFixtures.Entry, reversalId: EntryFixtures.Entry));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.NotReversible);
    }

    [Fact]
    public async Task HandleAsync_RepositoryReportsTheRaceAsAlreadyReversed_PassesItAheadWithoutAnEvent()
    {
        var harness = HarnessWithOriginal();
        harness.ApplyReturns(EntryErrors.AlreadyReversed);

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
        await AssertNothingWrittenAsync(harness);
    }

    [Fact]
    public async Task HandleAsync_SpentCredit_ReturnsInsufficientFundsWithoutWritingAnything()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(EntryType.Credit, 300.00m));
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(20m));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        await AssertNothingWrittenAsync(harness);
        harness.Operation.Received(1).Rejected("insufficient_funds");
        harness.ReverseLogger.Single(1004).Level.ShouldBe(LogLevel.Information);
    }

    [Fact]
    public async Task HandleAsync_SpentCreditWithNoOtherReversal_RereadsTheOriginalAndKeepsInsufficientFunds()
    {
        var harness = HarnessWithOriginal(EntryFixtures.Candidate(EntryType.Credit, 300.00m));
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(20m));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
        await harness.Scope.Entries.Received(2)
            .FindForReversalAsync(EntryFixtures.Account, EntryFixtures.OriginalEntry, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(20)]
    [InlineData(500)]
    public async Task HandleAsync_ConcurrentReversalSpentTheCredit_ReturnsAlreadyReversedAndNoEvent(int balance)
    {
        var harness = new WriteHarness();
        harness.Scope.Entries
            .FindForReversalAsync(Arg.Any<AccountId>(), Arg.Any<EntryId>(), Arg.Any<CancellationToken>())
            .Returns(
                EntryFixtures.Candidate(EntryType.Credit, 80.00m),
                EntryFixtures.Candidate(EntryType.Credit, 80.00m, reversalId: EntryFixtures.Entry));
        harness.ApplyReturns(ApplyErrors.NotMatched, ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(balance));

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
        await AssertNothingWrittenAsync(harness);
        await harness.Scope.Entries.Received(2)
            .FindForReversalAsync(EntryFixtures.Account, EntryFixtures.OriginalEntry, Arg.Any<CancellationToken>());
        harness.Operation.Received(1).Rejected("entry_already_reversed");
        harness.Operation.DidNotReceive().Rejected("insufficient_funds");
    }

    [Fact]
    public async Task HandleAsync_NotMatchedWithoutAnAccount_ReturnsAccountNotFound()
    {
        var harness = HarnessWithOriginal();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(null);

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public async Task HandleAsync_InvalidDescription_ReturnsTheDomainError()
    {
        var harness = HarnessWithOriginal();

        var result = await harness.Reverse.HandleAsync(
            EntryFixtures.ReverseCommand(description: new string('x', 141)),
            CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InvalidDescription);
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReplayWithTheSameRequest_ReturnsTheStoredEntryWithoutReadingTheOriginal()
    {
        var harness = HarnessWithOriginal();
        var command = EntryFixtures.ReverseCommand();
        var record = new IdempotencyRecord(CanonicalRequestHash.ForReversal(command), 1, EntryFixtures.StoredView());
        harness.ReserveReturns(false);
        harness.FindReturns(record);

        var result = await harness.Reverse.HandleAsync(command, CancellationToken.None);

        result.Value.IsReplay.ShouldBeTrue();
        result.Value.Entry.ShouldBe(record.Entry);
        harness.Scope.RollbackMarked.ShouldBeTrue();
        await harness.Scope.Entries.DidNotReceive()
            .FindForReversalAsync(Arg.Any<AccountId>(), Arg.Any<EntryId>(), Arg.Any<CancellationToken>());
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.Operation.Received(1).Replayed();
    }

    [Fact]
    public async Task HandleAsync_SameKeyWithAnotherDescription_ReturnsIdempotencyKeyReused()
    {
        var harness = HarnessWithOriginal();
        var original = EntryFixtures.ReverseCommand(description: "primeiro motivo");
        var record = new IdempotencyRecord(CanonicalRequestHash.ForReversal(original), 1, EntryFixtures.StoredView());
        harness.ReserveReturns(false);
        harness.FindReturns(record);

        var result = await harness.Reverse.HandleAsync(
            EntryFixtures.ReverseCommand(description: "segundo motivo"),
            CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyReused);
        harness.Operation.Received(1).IdempotencyConflict();
        var log = harness.ReverseLogger.Single(1003);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Operation"].ShouldBe("reverse");
    }

    [Fact]
    public async Task HandleAsync_KeyOfARegistrationUsedInAReversal_ReturnsIdempotencyKeyReused()
    {
        var harness = HarnessWithOriginal();
        var registration = new IdempotencyRecord(
            CanonicalRequestHash.ForRegistration(EntryFixtures.RegisterCommand()),
            1,
            EntryFixtures.StoredView());
        harness.ReserveReturns(false);
        harness.FindReturns(registration);

        var result = await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyReused);
    }

    [Fact]
    public async Task HandleAsync_Accepted_CountsAReversalAndLogsAtDebug()
    {
        var harness = HarnessWithOriginal();

        await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        harness.Telemetry.Received(1).Begin("reversal");
        harness.Operation.Received(1).Recorded("reversal");
        harness.Operation.Received(1).Dispose();
        harness.ReverseLogger.Single(1001).Level.ShouldBe(LogLevel.Debug);
    }

    [Fact]
    public async Task HandleAsync_RecordedInstantWasPushed_CountsTheCorrection()
    {
        var harness = HarnessWithOriginal();
        harness.Scope.Entries
            .TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(
                new AppliedEntry(EntryFixtures.ViewOf(call.Arg<NewEntry>()), true)));

        await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        harness.Operation.Received(1).RecordedAtCorrected();
        harness.ReverseLogger.Single(1005).Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task HandleAsync_CanceledToken_ThrowsBeforeOpeningTheTransaction()
    {
        var harness = HarnessWithOriginal();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), source.Token));

        harness.UnitOfWork.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_FunctionRunsTwice_CountsTheReversalOnlyOnce()
    {
        var harness = new WriteHarness(runs: 2);
        harness.OriginalReturns(EntryFixtures.Candidate());

        await harness.Reverse.HandleAsync(EntryFixtures.ReverseCommand(), CancellationToken.None);

        harness.Operation.Received(1).Recorded("reversal");
    }
}
