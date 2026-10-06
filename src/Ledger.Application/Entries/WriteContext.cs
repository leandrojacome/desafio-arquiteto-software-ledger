using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

internal sealed record WriteContext(
    AccountId AccountId,
    IdempotencyKey Key,
    EntryId EntryId,
    Guid EventId,
    byte[] RequestHash,
    string ClientId,
    string CorrelationId,
    string? TraceParent)
{
    public static WriteContext Create(
        IIdGenerator idGenerator,
        AccountId accountId,
        IdempotencyKey key,
        byte[] requestHash,
        string clientId,
        string correlationId,
        string? traceParent)
    {
        var entryId = EntryId.From(idGenerator.NewId());
        var eventId = idGenerator.NewId();

        if (entryId.IsFailure || eventId == Guid.Empty)
        {
            throw new InvalidOperationException("The id generator returned an empty identifier.");
        }

        return new WriteContext(
            accountId,
            key,
            entryId.Value,
            eventId,
            requestHash,
            clientId,
            correlationId,
            traceParent);
    }
}
