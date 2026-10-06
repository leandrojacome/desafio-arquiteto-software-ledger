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
public sealed class RegisterEntryReplayTests
{
    private static IdempotencyRecord StoredRecord(RegisterEntryCommand command, int hashVersion = 1) =>
        new(CanonicalRequestHash.ForRegistration(command), hashVersion, EntryFixtures.StoredView());

    [Fact]
    public async Task HandleAsync_KeyAlreadyUsedWithTheSameRequest_ReturnsTheStoredEntryAsAReplay()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        var record = StoredRecord(command);
        harness.ReserveReturns(false);
        harness.FindReturns(record);

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeTrue();
        result.Value.Entry.ShouldBe(record.Entry);
    }

    [Fact]
    public async Task HandleAsync_Replay_MarksRollbackAndNeitherAppliesNorEnqueues()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(command));

        await harness.Register.HandleAsync(command, CancellationToken.None);

        harness.Scope.RollbackMarked.ShouldBeTrue();
        harness.UnitOfWork.Commits.ShouldBe(0);
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        await harness.Scope.Accounts.DidNotReceive().GetForDiagnosisAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_Replay_CountsReplayedAndLogsTheFingerprint()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(command));

        await harness.Register.HandleAsync(command, CancellationToken.None);

        harness.Operation.Received(1).Replayed();
        harness.Operation.DidNotReceive().Recorded(Arg.Any<string>());
        var log = harness.RegisterLogger.Single(1002);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["KeyFingerprint"].ShouldBe(EntryFixtures.KeyFingerprint);
        log.Properties["EntryId"].ShouldBe(EntryFixtures.Entry.ToString());
        log.Message.ShouldNotContain(EntryFixtures.KeyText);
    }

    [Fact]
    public async Task HandleAsync_KeyAlreadyUsedWithADifferentRequest_ReturnsIdempotencyKeyReused()
    {
        var harness = new WriteHarness();
        var original = EntryFixtures.RegisterCommand();
        var changed = EntryFixtures.RegisterCommand(amount: 90.00m);
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(original));

        var result = await harness.Register.HandleAsync(changed, CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyReused);
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_KeyReused_CountsTheConflictAndLogsAWarning()
    {
        var harness = new WriteHarness();
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(EntryFixtures.RegisterCommand()));

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(amount: 90.00m), CancellationToken.None);

        harness.Operation.Received(1).IdempotencyConflict();
        harness.Operation.DidNotReceive().Rejected(Arg.Any<string>());
        var log = harness.RegisterLogger.Single(1003);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["KeyFingerprint"].ShouldBe(EntryFixtures.KeyFingerprint);
        log.Properties["ClientId"].ShouldBe(EntryFixtures.ClientId);
        log.Properties["Operation"].ShouldBe("register");
        log.Message.ShouldNotContain(EntryFixtures.KeyText);
    }

    [Fact]
    public async Task HandleAsync_KeyOfAReversalUsedInARegistration_ReturnsIdempotencyKeyReused()
    {
        var harness = new WriteHarness();
        var reversalRecord = new IdempotencyRecord(
            CanonicalRequestHash.ForReversal(EntryFixtures.ReverseCommand()),
            1,
            EntryFixtures.StoredView());
        harness.ReserveReturns(false);
        harness.FindReturns(reversalRecord);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyReused);
    }

    [Fact]
    public async Task HandleAsync_RecordMissingOnce_RestartsTheSequenceAndRecordsAsANewRequest()
    {
        var harness = new WriteHarness();
        harness.Scope.IdempotencyKeys
            .TryReserveAsync(
                Arg.Any<AccountId>(),
                Arg.Any<IdempotencyKey>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<int>(),
                Arg.Any<EntryId>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(false), Result.Success(true));
        harness.FindReturns(null);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplay.ShouldBeFalse();
        await harness.Scope.IdempotencyKeys.Received(1)
            .FindAsync(Arg.Any<AccountId>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
        await harness.Scope.Entries.Received(1).TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_RecordMissingTwice_ThrowsInvalidOperationException()
    {
        var harness = new WriteHarness();
        harness.ReserveReturns(false);
        harness.FindReturns(null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None));

        await harness.Scope.IdempotencyKeys.Received(2)
            .FindAsync(Arg.Any<AccountId>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>());
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task HandleAsync_UnknownHashVersion_ThrowsInvalidOperationException(int version)
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        harness.ReserveReturns(false);
        harness.FindReturns(StoredRecord(command, version));

        await Should.ThrowAsync<InvalidOperationException>(
            () => harness.Register.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_Replay_RecomputesTheHashWithTheCurrentAlgorithm()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        harness.ReserveReturns(false);
        harness.FindReturns(new IdempotencyRecord(new byte[32], 1, EntryFixtures.StoredView()));

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.IdempotencyKeyReused);
    }
}
