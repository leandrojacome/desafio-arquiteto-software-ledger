using System.Globalization;
using Ledger.Application;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal static class PostgresConnectionString
{
    private const int ConnectionIdleLifetimeSeconds = 300;
    private const int ConnectionPruningIntervalSeconds = 10;
    private const int MaxAutoPrepareStatements = 20;
    private const int AutoPrepareMinUsages = 2;

    public static string Build(PostgresOptions options, PostgresSource source)
    {
        var settings = options.For(source);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = options.Host,
            Port = options.Port,
            Database = options.Database,
            Username = settings.Username,
            Password = settings.Password,
            ApplicationName = ApplicationNameFor(source),
            SslMode = options.SslMode,
            GssEncryptionMode = GssEncryptionMode.Disable,
            IncludeErrorDetail = options.IncludeErrorDetail,
            MaxPoolSize = settings.MaxPoolSize,
            MinPoolSize = settings.MinPoolSize,
            Timeout = settings.ConnectionTimeoutSeconds,
            CommandTimeout = settings.CommandTimeoutSeconds,
            Multiplexing = false,
            TcpKeepAlive = true,
            ConnectionIdleLifetime = ConnectionIdleLifetimeSeconds,
            ConnectionPruningInterval = ConnectionPruningIntervalSeconds,
            MaxAutoPrepare = MaxAutoPrepareStatements,
            AutoPrepareMinUsages = AutoPrepareMinUsages,
            Options = ServerOptionsFor(settings)
        };

        return builder.ConnectionString;
    }

    private static string ApplicationNameFor(PostgresSource source)
    {
        return source switch
        {
            PostgresSource.Write => "ledger-api-write",
            PostgresSource.Balance => "ledger-api-balance",
            PostgresSource.Statement => "ledger-api-statement",
            PostgresSource.Worker => ServiceIdentity.Worker,
            PostgresSource.Migrator => "ledger-migrator",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown PostgreSQL source.")
        };
    }

    private static string ServerOptionsFor(PostgresSourceOptions settings)
    {
        var parts = new List<string> { "-c timezone=UTC" };

        if (settings.LockTimeoutMs is { } lockTimeoutMs)
        {
            parts.Add(Setting("lock_timeout", lockTimeoutMs));
        }

        parts.Add(Setting("statement_timeout", settings.StatementTimeoutMs));
        parts.Add(Setting("idle_in_transaction_session_timeout", settings.IdleInTransactionTimeoutMs));

        if (settings.ReadOnly)
        {
            parts.Add("-c default_transaction_read_only=on");
        }

        return string.Join(' ', parts);
    }

    private static string Setting(string name, int milliseconds)
    {
        return string.Create(CultureInfo.InvariantCulture, $"-c {name}={milliseconds}");
    }
}
