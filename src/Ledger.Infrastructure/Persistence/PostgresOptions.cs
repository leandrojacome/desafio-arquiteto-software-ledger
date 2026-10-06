using System.ComponentModel.DataAnnotations;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    [Required] public string Host { get; init; } = string.Empty;

    [Range(1, 65_535)] public int Port { get; init; } = 5432;

    [Required] public string Database { get; init; } = string.Empty;

    public SslMode SslMode { get; init; } = SslMode.Disable;

    public bool IncludeErrorDetail { get; init; }

    public PostgresSourcesOptions Sources { get; init; } = new();

    internal PostgresSourceOptions For(PostgresSource source)
    {
        return source switch
        {
            PostgresSource.Write => Sources.Write,
            PostgresSource.Balance => Sources.Balance,
            PostgresSource.Statement => Sources.Statement,
            PostgresSource.Worker => Sources.Worker,
            PostgresSource.Migrator => Sources.Migrator,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown PostgreSQL source.")
        };
    }
}

internal sealed class PostgresSourcesOptions
{
    public PostgresSourceOptions Write { get; init; } = new();

    public PostgresSourceOptions Balance { get; init; } = new();

    public PostgresSourceOptions Statement { get; init; } = new();

    public PostgresSourceOptions Worker { get; init; } = new();

    public PostgresSourceOptions Migrator { get; init; } = new();
}

internal sealed class PostgresSourceOptions
{
    [Required] public string Username { get; init; } = string.Empty;

    [Required] public string Password { get; init; } = string.Empty;

    [Range(1, 500)] public int MaxPoolSize { get; init; } = 5;

    [Range(0, 500)] public int MinPoolSize { get; init; }

    [Range(1, 60)] public int ConnectionTimeoutSeconds { get; init; } = 1;

    [Range(1, 3600)] public int CommandTimeoutSeconds { get; init; } = 2;

    [Range(1, 600_000)] public int? LockTimeoutMs { get; init; }

    [Range(1, 3_600_000)] public int StatementTimeoutMs { get; init; } = 2500;

    [Range(1, 3_600_000)] public int IdleInTransactionTimeoutMs { get; init; } = 5000;

    public bool ReadOnly { get; init; }
}
