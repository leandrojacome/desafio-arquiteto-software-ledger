using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IEntryRepository
{
    Task<Result<AppliedEntry>> TryApplyAsync(NewEntry entry, CancellationToken cancellationToken);

    Task<ReversalCandidate?> FindForReversalAsync(
        AccountId accountId,
        EntryId entryId,
        CancellationToken cancellationToken);
}
