using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

internal sealed class SecurityDatabase : IAsyncDisposable
{
    private readonly EmptyDatabase _database;
    private readonly PostgresConnectionFactory _factory;
    private NpgsqlConnection? _workerReader;

    private SecurityDatabase(EmptyDatabase database, PostgresConnectionFactory factory)
    {
        _database = database;
        _factory = factory;
    }

    public IPostgresConnectionFactory Connections => _factory;

    public PostgresOptions Settings => _database.Settings;

    [SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the isolated database and of the connection factory passes to the returned SecurityDatabase.")]
    public static async Task<SecurityDatabase> CreateAsync(PostgresFixture postgres)
    {
        var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        var report = await database.MigrateAsync(CancellationToken.None);

        if (!report.Succeeded)
        {
            await database.DisposeAsync();

            throw new InvalidOperationException("The migrations of the isolated database failed.");
        }

        return new SecurityDatabase(database, PostgresFixture.CreateConnectionFactory(database.Settings));
    }

    public Task<NpgsqlConnection> OpenAdministrativeAsync() =>
        _database.OpenAdministrativeConnectionAsync(CancellationToken.None);

    public async Task<NpgsqlConnection> OpenAsRoleAsync(string role)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Settings.Host,
            Port = Settings.Port,
            Database = Settings.Database,
            Username = role,
            Password = PostgresFixture.RolePassword,
            Pooling = false
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);

        await connection.OpenAsync(CancellationToken.None);

        return connection;
    }

    public async Task<NpgsqlConnection> WorkerReaderAsync()
    {
        _workerReader ??= await OpenAsRoleAsync(PostgresFixture.WorkerRole);

        return _workerReader;
    }

    public async ValueTask DisposeAsync()
    {
        if (_workerReader is not null)
        {
            await _workerReader.DisposeAsync();
        }

        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }
}
