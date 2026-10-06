namespace Ledger.Application.Abstractions;

public interface IDatabaseMigrator
{
    Task<MigrationStatus> MigrateAsync(CancellationToken cancellationToken);
}
