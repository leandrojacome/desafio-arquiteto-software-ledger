using Ledger.Application.Accounts;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Abstractions;

public interface IUnitOfWork
{
    Task<Result<TValue>> ExecuteAsync<TValue>(
        Func<IUnitOfWorkScope, CancellationToken, Task<Result<TValue>>> work,
        CancellationToken cancellationToken)
        where TValue : notnull;
}

public interface IUnitOfWorkScope
{
    IAccountRepository Accounts { get; }

    IEntryRepository Entries { get; }

    IIdempotencyStore IdempotencyKeys { get; }

    IOutbox Outbox { get; }

    IAuditTrail Audit { get; }

    void MarkForRollback();
}

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

public interface IEntryRepository
{
    Task<Result<AppliedEntry>> TryApplyAsync(NewEntry entry, CancellationToken cancellationToken);

    Task<ReversalCandidate?> FindForReversalAsync(
        AccountId accountId,
        EntryId entryId,
        CancellationToken cancellationToken);
}
