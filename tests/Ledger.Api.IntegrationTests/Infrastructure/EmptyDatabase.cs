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
