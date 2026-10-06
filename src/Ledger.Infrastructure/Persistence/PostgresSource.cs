namespace Ledger.Infrastructure.Persistence;

public enum PostgresSource
{
    Write,
    Balance,
    Statement,
    Worker,
    Migrator
}
