using System.Text;
using Ledger.Application.Entries;
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

public sealed record IdempotencyRecord(ReadOnlyMemory<byte> RequestHash, int HashVersion, EntryView Entry)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("EntryId = ").Append(Entry.Id).Append(", HashVersion = ").Append(HashVersion);

        return true;
    }
}

public interface IIdempotencyKeyPruner
{
    Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken);
}
