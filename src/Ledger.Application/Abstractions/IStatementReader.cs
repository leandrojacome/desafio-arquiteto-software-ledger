using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Abstractions;

public interface IStatementReader
{
    Task<IReadOnlyList<EntryView>> ReadPageAsync(
        AccountId accountId,
        StatementBounds bounds,
        int limitPlusOne,
        CancellationToken cancellationToken);

    Task<bool> AccountExistsAsync(AccountId accountId, CancellationToken cancellationToken);
}
