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
