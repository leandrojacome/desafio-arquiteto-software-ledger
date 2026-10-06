namespace Ledger.Application.Abstractions;

public interface IDatabaseMigrator
{
    Task<MigrationStatus> MigrateAsync(CancellationToken cancellationToken);
}

public enum MigrationStatus
{
    Succeeded,
    ScriptFailed,
    ConnectionFailed,
    LockTimedOut
}
