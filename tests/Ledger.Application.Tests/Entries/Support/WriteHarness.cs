using Ledger.Application.Abstractions;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using NSubstitute;

namespace Ledger.Application.Tests.Entries.Support;

internal sealed class WriteHarness
{
    public WriteHarness(int runs = 1)
    {
        UnitOfWork = new InlineUnitOfWork(runs);

        Ids.NewId().Returns(EntryFixtures.EntryGuid, EntryFixtures.EventGuid);
        Telemetry.Begin(Arg.Any<string>()).Returns(Operation);

        Scope.IdempotencyKeys
            .TryReserveAsync(
                Arg.Any<AccountId>(),
                Arg.Any<IdempotencyKey>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<int>(),
                Arg.Any<EntryId>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));

        Scope.Entries
            .TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(new AppliedEntry(EntryFixtures.ViewOf(call.Arg<NewEntry>()), false)));

        Register = new RegisterEntryHandler(UnitOfWork, Ids, Telemetry, RegisterLogger);
        Reverse = new ReverseEntryHandler(UnitOfWork, Ids, Telemetry, ReverseLogger);
    }

    public InlineUnitOfWork UnitOfWork { get; }

    public RecordingScope Scope => UnitOfWork.Scope;

    public IIdGenerator Ids { get; } = Substitute.For<IIdGenerator>();

    public IEntryTelemetry Telemetry { get; } = Substitute.For<IEntryTelemetry>();

    public IEntryOperation Operation { get; } = Substitute.For<IEntryOperation>();

    public CapturingLogger<RegisterEntryHandler> RegisterLogger { get; } = new();

    public CapturingLogger<ReverseEntryHandler> ReverseLogger { get; } = new();

    public RegisterEntryHandler Register { get; }

    public ReverseEntryHandler Reverse { get; }

    public void ReserveReturns(Result<bool> result)
    {
        Scope.IdempotencyKeys
            .TryReserveAsync(
                Arg.Any<AccountId>(),
                Arg.Any<IdempotencyKey>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<int>(),
                Arg.Any<EntryId>(),
                Arg.Any<CancellationToken>())
            .Returns(result);
    }

    public void ApplyReturns(params Result<AppliedEntry>[] results)
    {
        Scope.Entries
            .TryApplyAsync(Arg.Any<NewEntry>(), Arg.Any<CancellationToken>())
            .Returns(results[0], results[1..]);
    }

    public void FindReturns(IdempotencyRecord? record)
    {
        Scope.IdempotencyKeys
            .FindAsync(Arg.Any<AccountId>(), Arg.Any<IdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(record);
    }

    public void DiagnosisReturns(AccountBalance? snapshot)
    {
        Scope.Accounts
            .GetForDiagnosisAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(snapshot);
    }

    public void OriginalReturns(ReversalCandidate? candidate)
    {
        Scope.Entries
            .FindForReversalAsync(Arg.Any<AccountId>(), Arg.Any<EntryId>(), Arg.Any<CancellationToken>())
            .Returns(candidate);
    }
}
