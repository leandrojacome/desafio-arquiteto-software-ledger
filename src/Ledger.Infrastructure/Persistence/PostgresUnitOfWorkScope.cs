using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence.Outbox;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresUnitOfWorkScope : IUnitOfWorkScope
{
    public PostgresUnitOfWorkScope(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ISecurityTelemetry securityTelemetry)
    {
        Connection = connection;
        Transaction = transaction;
        Accounts = new PostgresAccountRepository(connection, transaction);
        Entries = new PostgresEntryRepository(connection, transaction);
        IdempotencyKeys = new PostgresIdempotencyStore(connection, transaction);
        Outbox = new PostgresOutbox(connection, transaction);
        Audit = new PostgresScopedAuditTrail(connection, transaction, securityTelemetry);
    }

    internal NpgsqlConnection Connection { get; }

    internal NpgsqlTransaction Transaction { get; }

    public IAccountRepository Accounts { get; }

    public IEntryRepository Entries { get; }

    public IIdempotencyStore IdempotencyKeys { get; }

    public IOutbox Outbox { get; }

    public IAuditTrail Audit { get; }

    public bool IsRollbackRequested { get; private set; }

    public void MarkForRollback()
    {
        IsRollbackRequested = true;
    }
}
