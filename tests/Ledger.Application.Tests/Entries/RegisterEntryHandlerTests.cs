using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class RegisterEntryHandlerTests
{
    [Theory]
    [InlineData(EntryType.Credit)]
    [InlineData(EntryType.Debit)]
    public async Task HandleAsync_NewEntry_AppliesTheEntryWithClientAndCorrelationOfTheCommand(EntryType type)
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand(type);

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var applied = harness.Scope.Entries.ReceivedCalls().Single().GetArguments().OfType<NewEntry>().Single();
        applied.ClientId.ShouldBe(EntryFixtures.ClientId);
        applied.CorrelationId.ShouldBe(EntryFixtures.CorrelationId);
        applied.Entry.Type.ShouldBe(type);
        applied.Entry.Id.ShouldBe(EntryFixtures.Entry);
        applied.Entry.AccountId.ShouldBe(EntryFixtures.Account);
        applied.Entry.Amount.ShouldBe(command.Amount);
        applied.Entry.OccurredAt.ShouldBe(command.OccurredAt);
        applied.Entry.Description.ShouldBe(EntryFixtures.Description);
        applied.Entry.Reference.ShouldBe(EntryFixtures.Reference);
        applied.Entry.ReversesEntryId.ShouldBeNull();
    }

    [Fact]
    public async Task HandleAsync_NewEntry_ReservesAppliesAndEnqueuesInOrderAndCommitsOnce()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();
        var expectedHash = CanonicalRequestHash.ForRegistration(command);

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            harness.Scope.IdempotencyKeys.TryReserveAsync(
                EntryFixtures.Account,
                EntryFixtures.Key,
                Arg.Is<ReadOnlyMemory<byte>>(hash => hash.ToArray().SequenceEqual(expectedHash)),
                CanonicalRequestHash.CurrentVersion,
                EntryFixtures.Entry,
                Arg.Any<CancellationToken>());
            harness.Scope.Entries.TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
            harness.Scope.Outbox.EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        });
        harness.UnitOfWork.Commits.ShouldBe(1);
        harness.UnitOfWork.Rollbacks.ShouldBe(0);
        harness.UnitOfWork.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_NewEntry_ReturnsTheViewTheRepositoryProducedAsANonReplay()
    {
        var harness = new WriteHarness();

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        var outcome = result.Value;
        outcome.IsReplay.ShouldBeFalse();
        outcome.Entry.Id.ShouldBe(EntryFixtures.Entry);
        outcome.Entry.AccountVersion.ShouldBe(1843);
        outcome.Entry.BalanceAfter.ShouldBe(EntryFixtures.Brl(920.00m));
    }

    [Fact]
    public async Task HandleAsync_NewEntry_EnqueuesTheEventWithTheContractFields()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand();

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        var message = harness.Scope.Outbox.ReceivedCalls().Single().GetArguments().OfType<OutboxMessage>().Single();
        message.Id.ShouldBe(EntryFixtures.EventGuid);
        message.AccountId.ShouldBe(EntryFixtures.Account);
        message.Type.ShouldBe("EntryRegistered");
        message.CorrelationId.ShouldBe(EntryFixtures.CorrelationId);
        message.TraceParent.ShouldBe(EntryFixtures.TraceParent);
        message.Payload.ShouldBe(
            EntryRegisteredPayload.Serialize(result.Value.Entry, EntryFixtures.EventGuid, EntryFixtures.CorrelationId));
    }

    [Fact]
    public async Task HandleAsync_NewEntry_GeneratesEntryAndEventIdsBeforeTheTransaction()
    {
        var harness = new WriteHarness();
        var generatedBeforeTransaction = -1;
        harness.UnitOfWork.BeforeExecute = () => generatedBeforeTransaction = harness.Ids.ReceivedCalls().Count();

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        generatedBeforeTransaction.ShouldBe(2);
        harness.Ids.Received(2).NewId();
    }

    [Fact]
    public async Task HandleAsync_IdGeneratorReturnsEmptyGuid_Throws()
    {
        var harness = new WriteHarness();
        harness.Ids.NewId().Returns(Guid.Empty);

        await Should.ThrowAsync<InvalidOperationException>(() => harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None));
        harness.UnitOfWork.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_ReservationFailsWithAccountNotFound_ReturnsItWithoutApplyingOrEnqueuing()
    {
        var harness = new WriteHarness();
        harness.ReserveReturns(AccountErrors.NotFound);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(AccountErrors.NotFound);
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Commits.ShouldBe(0);
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
        harness.Operation.Received(1).Rejected("account_not_found");
    }

    [Fact]
    public async Task HandleAsync_RepositoryReturnsAlreadyReversed_PassesItAheadWithoutTouchingTheOutbox()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(EntryErrors.AlreadyReversed);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.AlreadyReversed);
        await harness.Scope.Outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>());
        harness.UnitOfWork.Rollbacks.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_InvalidDescription_ReturnsTheDomainErrorAndNeverAppliesTheEntry()
    {
        var harness = new WriteHarness();
        var command = EntryFixtures.RegisterCommand(description: new string('x', 141));

        var result = await harness.Register.HandleAsync(command, CancellationToken.None);

        result.Error.ShouldBe(EntryErrors.InvalidDescription);
        await harness.Scope.Entries.DidNotReceive().TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>());
        harness.Operation.Received(1).Rejected("validation");
    }

    [Fact]
    public async Task HandleAsync_AnyOutcome_NeverReturnsTheInternalNotMatchedError()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldNotBe(ApplyErrors.NotMatched);
        result.Error.ShouldBe(EntryErrors.InsufficientFunds);
    }

    [Fact]
    public async Task HandleAsync_CanceledToken_ThrowsWithoutOpeningTheTransaction()
    {
        var harness = new WriteHarness();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), source.Token));
        harness.UnitOfWork.Calls.ShouldBe(0);
        harness.Telemetry.DidNotReceive().Begin(Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_TokenIsPassedToEveryPort()
    {
        var harness = new WriteHarness();
        using var source = new CancellationTokenSource();

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), source.Token);

        await harness.Scope.IdempotencyKeys.Received(1).TryReserveAsync(
            Arg.Any<AccountId>(),
            Arg.Any<IdempotencyKey>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<int>(),
            Arg.Any<EntryId>(),
            source.Token);
        await harness.Scope.Entries.Received(1).TryApplyAsync(Arg.Any<NewEntry>(), source.Token);
        await harness.Scope.Outbox.Received(1).EnqueueAsync(Arg.Any<OutboxMessage>(), source.Token);
    }

    [Fact]
    public async Task HandleAsync_UnexpectedException_PropagatesAndDisposesTheOperationWithoutAnyOutcome()
    {
        var harness = new WriteHarness();
        harness.Scope.Entries
            .TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None));
        harness.Operation.Received(1).Dispose();
        harness.Operation.DidNotReceive().Recorded(Arg.Any<string>());
        harness.Operation.DidNotReceive().Rejected(Arg.Any<string>());
        harness.Operation.DidNotReceive().Replayed();
        harness.Operation.DidNotReceive().IdempotencyConflict();
    }

    [Fact]
    public async Task HandleAsync_FunctionRunsTwice_ReusesTheKeyTheHashAndTheIds()
    {
        var harness = new WriteHarness(runs: 2);

        var result = await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        harness.UnitOfWork.Executions.ShouldBe(2);
        harness.Ids.Received(2).NewId();
        var reservations = harness.Scope.IdempotencyKeys.ReceivedCalls().Select(call => call.GetArguments()).ToList();
        reservations.Count.ShouldBe(2);
        reservations[0][1].ShouldBe(reservations[1][1]);
        reservations[0][4].ShouldBe(reservations[1][4]);
        ((ReadOnlyMemory<byte>)reservations[0][2]!).ToArray().ShouldBe(((ReadOnlyMemory<byte>)reservations[1][2]!).ToArray());
        var events = harness.Scope.Outbox.ReceivedCalls()
            .Select(call => call.GetArguments().OfType<OutboxMessage>().Single().Id)
            .ToList();
        events.Count.ShouldBe(2);
        events.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_FunctionRunsTwice_CountsTheEntryOnlyOnce()
    {
        var harness = new WriteHarness(runs: 2);

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        harness.UnitOfWork.Executions.ShouldBe(2);
        harness.Operation.Received(1).Recorded("debit");
        harness.RegisterLogger.Entries.Count(entry => entry.EventId.Id == 1001).ShouldBe(1);
    }

    [Theory]
    [InlineData(EntryType.Credit, "credit")]
    [InlineData(EntryType.Debit, "debit")]
    public async Task HandleAsync_Accepted_OpensTheOperationByTypeAndCountsRecorded(EntryType type, string label)
    {
        var harness = new WriteHarness();

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(type), CancellationToken.None);

        harness.Telemetry.Received(1).Begin(label);
        harness.Operation.Received(1).Recorded(label);
        harness.Operation.Received(1).Dispose();
        harness.Operation.DidNotReceive().Replayed();
        harness.Operation.DidNotReceive().RecordedAtCorrected();
    }

    [Fact]
    public async Task HandleAsync_Accepted_LogsEventOneThousandOneAtDebugWithoutAmountOrFreeText()
    {
        var harness = new WriteHarness();

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        var log = harness.RegisterLogger.Single(1001);
        log.Level.ShouldBe(LogLevel.Debug);
        log.Properties["EntryId"].ShouldBe(EntryFixtures.Entry.ToString());
        log.Properties["AccountId"].ShouldBe(EntryFixtures.Account.ToString());
        log.Properties["ClientId"].ShouldBe(EntryFixtures.ClientId);
        log.Message.ShouldNotContain("80");
        log.Message.ShouldNotContain(EntryFixtures.Description);
        log.Message.ShouldNotContain(EntryFixtures.Reference);
    }

    [Fact]
    public async Task HandleAsync_RecordedInstantWasPushed_CountsTheCorrectionAndLogsAWarning()
    {
        var harness = new WriteHarness();
        harness.Scope.Entries
            .TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new AppliedEntry(EntryFixtures.ViewOf(call.Arg<NewEntry>()), true)));

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        harness.Operation.Received(1).RecordedAtCorrected();
        harness.Operation.Received(1).Recorded("debit");
        var log = harness.RegisterLogger.Single(1005);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["ClientId"].ShouldBe(EntryFixtures.ClientId);
    }

    [Fact]
    public async Task HandleAsync_InsufficientFunds_CountsTheRejectionAndLogsTheFingerprintNotTheKey()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        harness.Operation.Received(1).Rejected("insufficient_funds");
        var log = harness.RegisterLogger.Single(1004);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["KeyFingerprint"].ShouldBe(EntryFixtures.KeyFingerprint);
        log.Message.ShouldNotContain(EntryFixtures.KeyText);
        log.Message.ShouldNotContain("80");
    }

    [Fact]
    public async Task HandleAsync_NoEventIsLoggedAboveInformationWithMoneyOrFreeText()
    {
        var harness = new WriteHarness();
        harness.ApplyReturns(ApplyErrors.NotMatched);
        harness.DiagnosisReturns(EntryFixtures.AccountSnapshot(10m));

        await harness.Register.HandleAsync(EntryFixtures.RegisterCommand(), CancellationToken.None);

        foreach (var entry in harness.RegisterLogger.Entries.Where(entry => entry.Level >= LogLevel.Information))
        {
            entry.Message.ShouldNotContain(EntryFixtures.KeyText);
            entry.Message.ShouldNotContain(EntryFixtures.Description);
            entry.Message.ShouldNotContain(EntryFixtures.Reference);
            entry.Properties.Values.ShouldNotContain(EntryFixtures.KeyText);
        }
    }
}
