using System.Globalization;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Infrastructure;

public sealed class EmptyDatabase : IAsyncDisposable
{
    private readonly PostgresFixture _owner;

    internal EmptyDatabase(string name, PostgresOptions settings, PostgresFixture owner)
    {
        Name = name;
        Settings = settings;
        _owner = owner;
    }

    public string Name { get; }

    internal PostgresOptions Settings { get; }

    internal Task<MigrationReport> MigrateAsync(CancellationToken cancellationToken) =>
        MigrateAsync(new MigrationOptions(), NullLogger<MigrationRunner>.Instance, cancellationToken);

    internal async Task<MigrationReport> MigrateAsync(
        MigrationOptions migration,
        ILogger<MigrationRunner> logger,
        CancellationToken cancellationToken)
    {
        await using var factory = PostgresFixture.CreateConnectionFactory(Settings);
        var runner = new MigrationRunner(
            factory,
            Options.Create(Settings),
            Options.Create(migration),
            TimeProvider.System,
            logger);

        return await runner.MigrateAsync(cancellationToken);
    }

    public Task<NpgsqlConnection> OpenAdministrativeConnectionAsync(CancellationToken cancellationToken) =>
        _owner.OpenAdministrativeConnectionToAsync(Name, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _owner.DropDatabaseAsync(Name, CancellationToken.None);
    }
}

internal static class MigrationLockHolder
{
    public static async Task<NpgsqlConnection> HoldAsync(EmptyDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", MigrationRunner.AdvisoryLockKey);

        await command.ExecuteNonQueryAsync(CancellationToken.None);

        return connection;
    }

    public static async Task ReleaseAsync(NpgsqlConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", MigrationRunner.AdvisoryLockKey);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public static async Task<long> CountMigratorSessionsAsync(EmptyDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND application_name = 'ledger-migrator'",
            connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }

    public static async Task<long> CountPublicTablesAsync(EmptyDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        await using var connection = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'",
            connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }
}
