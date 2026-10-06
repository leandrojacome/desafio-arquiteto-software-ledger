using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IBalanceReader
{
    Task<Result<CurrentBalanceReading>> ReadCurrentAsync(AccountId accountId, CancellationToken cancellationToken);

    Task<Result<BalanceAtReading>> ReadAtAsync(
        AccountId accountId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

public interface IStatementReader
{
    Task<IReadOnlyList<EntryView>> ReadPageAsync(
        AccountId accountId,
        StatementBounds bounds,
        int limitPlusOne,
        CancellationToken cancellationToken);

    Task<bool> AccountExistsAsync(AccountId accountId, CancellationToken cancellationToken);
}
