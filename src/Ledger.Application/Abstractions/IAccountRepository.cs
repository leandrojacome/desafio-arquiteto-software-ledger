using Ledger.Application.Accounts;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Abstractions;

public interface IAccountRepository
{
    Task<AccountBalance?> GetForDiagnosisAsync(AccountId accountId, CancellationToken cancellationToken);

    Task<DateTimeOffset?> CreateAsync(NewAccount account, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetCreatedAtAsync(AccountId accountId, CancellationToken cancellationToken);

    Task<bool> TryReserveCreationKeyAsync(
        AccountCreationKeyReservation reservation,
        CancellationToken cancellationToken);

    Task<AccountCreationKeyRecord?> FindCreationKeyAsync(
        string clientId,
        IdempotencyKey key,
        CancellationToken cancellationToken);
}
