using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IIdempotencyStore
{
    Task<Result<bool>> TryReserveAsync(
        AccountId accountId,
        IdempotencyKey key,
        ReadOnlyMemory<byte> requestHash,
        int hashVersion,
        EntryId entryId,
        CancellationToken cancellationToken);

    Task<IdempotencyRecord?> FindAsync(
        AccountId accountId,
        IdempotencyKey key,
        CancellationToken cancellationToken);
}
