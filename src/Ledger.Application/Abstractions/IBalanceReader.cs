using Ledger.Application.Balances;
using Ledger.Domain.Accounts;
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
