using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

internal static class EntryWriteFlow
{
    private const int MaxReservationAttempts = 2;

    public static async Task<Result<WrittenEntry>> RunAsync(
        IUnitOfWorkScope scope,
        WriteContext context,
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<Entry>>> prepare,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReservationAttempts; attempt++)
        {
            var reservation = await scope.IdempotencyKeys.TryReserveAsync(
                context.AccountId,
                context.Key,
                context.RequestHash,
                CanonicalRequestHash.CurrentVersion,
                context.EntryId,
                cancellationToken);

            if (reservation.IsFailure)
            {
                return reservation.Error;
            }

            if (reservation.Value)
            {
                return await ApplyNewAsync(scope, context, prepare, cancellationToken);
            }

            var replay = await ReplayAsync(scope, context, cancellationToken);

            if (replay is not null)
            {
                return replay;
            }
        }

        throw new InvalidOperationException(
            "The idempotency key is reserved by another request but its record could not be read back.");
    }

    private static async Task<Result<WrittenEntry>?> ReplayAsync(
        IUnitOfWorkScope scope,
        WriteContext context,
        CancellationToken cancellationToken)
    {
        var record = await scope.IdempotencyKeys.FindAsync(context.AccountId, context.Key, cancellationToken);

        if (record is null)
        {
            return null;
        }

        scope.MarkForRollback();

        if (record.HashVersion != CanonicalRequestHash.CurrentVersion)
        {
            throw new InvalidOperationException("The idempotency record uses an unknown request hash version.");
        }

        if (!CanonicalRequestHash.Matches(record.RequestHash.Span, context.RequestHash))
        {
            return EntryErrors.IdempotencyKeyReused;
        }

        return new WrittenEntry(record.Entry, true, false);
    }

    private static async Task<Result<WrittenEntry>> ApplyNewAsync(
        IUnitOfWorkScope scope,
        WriteContext context,
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<Entry>>> prepare,
        CancellationToken cancellationToken)
    {
        var prepared = await prepare(scope, cancellationToken);

        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        var newEntry = new NewEntry(prepared.Value, context.ClientId, context.CorrelationId);
        var applied = await ApplyWithRefusalAsync(scope, newEntry, cancellationToken);

        if (applied.IsFailure)
        {
            return await ExplainRefusalAsync(scope, prepared.Value, prepare, applied.Error, cancellationToken);
        }

        var payload = EntryRegisteredPayload.Serialize(applied.Value.Entry, context.EventId, context.CorrelationId);
        var message = new OutboxMessage(
            context.EventId,
            context.AccountId,
            EntryRegisteredPayload.EventType,
            payload,
            context.CorrelationId,
            context.TraceParent);

        await scope.Outbox.EnqueueAsync(message, cancellationToken);

        return new WrittenEntry(applied.Value.Entry, false, applied.Value.RecordedAtCorrected);
    }

    private static async Task<Result<WrittenEntry>> ExplainRefusalAsync(
        IUnitOfWorkScope scope,
        Entry prepared,
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<Entry>>> prepare,
        Error refusal,
        CancellationToken cancellationToken)
    {
        if (refusal != EntryErrors.InsufficientFunds || !prepared.IsReversal)
        {
            return refusal;
        }

        var current = await prepare(scope, cancellationToken);

        return current.IsFailure ? current.Error : refusal;
    }

    private static async Task<Result<AppliedEntry>> ApplyWithRefusalAsync(
        IUnitOfWorkScope scope,
        NewEntry newEntry,
        CancellationToken cancellationToken)
    {
        var first = await scope.Entries.TryApplyAsync(newEntry, cancellationToken);

        if (!IsNotMatched(first))
        {
            return first;
        }

        var diagnosis = await scope.Accounts.GetForDiagnosisAsync(newEntry.Entry.AccountId, cancellationToken);

        if (diagnosis is null)
        {
            return AccountErrors.NotFound;
        }

        var classification = diagnosis.Apply(newEntry.Entry.Type, newEntry.Entry.Amount);

        if (classification.IsFailure)
        {
            return classification.Error;
        }

        var second = await scope.Entries.TryApplyAsync(newEntry, cancellationToken);

        return IsNotMatched(second) ? EntryErrors.InsufficientFunds : second;
    }

    private static bool IsNotMatched(Result<AppliedEntry> result) =>
        result.IsFailure && result.Error == ApplyErrors.NotMatched;
}
