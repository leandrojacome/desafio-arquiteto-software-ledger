namespace Ledger.Application.Abstractions;

public interface IUnitOfWorkScope
{
    IAccountRepository Accounts { get; }

    IEntryRepository Entries { get; }

    IIdempotencyStore IdempotencyKeys { get; }

    IOutbox Outbox { get; }

    IAuditTrail Audit { get; }

    void MarkForRollback();
}
