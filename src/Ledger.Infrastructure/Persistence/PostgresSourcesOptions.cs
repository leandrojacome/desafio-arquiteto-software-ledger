namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresSourcesOptions
{
    public PostgresSourceOptions Write { get; init; } = new();

    public PostgresSourceOptions Balance { get; init; } = new();

    public PostgresSourceOptions Statement { get; init; } = new();

    public PostgresSourceOptions Worker { get; init; } = new();

    public PostgresSourceOptions Migrator { get; init; } = new();
}
