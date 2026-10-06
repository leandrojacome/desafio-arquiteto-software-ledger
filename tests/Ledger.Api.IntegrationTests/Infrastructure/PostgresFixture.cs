using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ledger.Api.IntegrationTests.Infrastructure;

[SuppressMessage("Design", "CA1001",
    Justification = "xUnit disposes collection fixtures through IAsyncLifetime.DisposeAsync.")]
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ApiRole = "ledger_api";
    public const string WorkerRole = "ledger_worker";
    public const string ReadOnlyRole = "ledger_readonly";
    public const string MigratorRole = "ledger_migrator";
    public const string RolePassword = "integration-tests-role-password";
    public const string Database = "ledger";

    private const string Image = "postgres:16";
    private const string SuperUser = "postgres";
    private const string SuperUserPassword = "integration-tests-superuser-password";
    private const string InitRolesScript = "init-roles.sh";
    private const string InitScriptsDirectory = "/docker-entrypoint-initdb.d/10-init-roles.sh";
    private const string SolutionFile = "Ledger.sln";
    private const uint ExecutableFileMode = 0b_111_101_101;

    private static readonly string[] AllSources = ["Write", "Balance", "Statement", "Worker", "Migrator"];

    public static IReadOnlyDictionary<string, string?> ProductionSources { get; } = new Dictionary<string, string?>
    {
        ["Postgres:Sources:Write:MaxPoolSize"] = "7",
        ["Postgres:Sources:Write:MinPoolSize"] = "2",
        ["Postgres:Sources:Write:ConnectionTimeoutSeconds"] = "1",
        ["Postgres:Sources:Write:CommandTimeoutSeconds"] = "2",
        ["Postgres:Sources:Write:LockTimeoutMs"] = "1000",
        ["Postgres:Sources:Write:StatementTimeoutMs"] = "2500",
        ["Postgres:Sources:Write:IdleInTransactionTimeoutMs"] = "5000",
        ["Postgres:Sources:Write:ReadOnly"] = "false",
        ["Postgres:Sources:Balance:MaxPoolSize"] = "8",
        ["Postgres:Sources:Balance:MinPoolSize"] = "2",
        ["Postgres:Sources:Balance:ConnectionTimeoutSeconds"] = "1",
        ["Postgres:Sources:Balance:CommandTimeoutSeconds"] = "1",
        ["Postgres:Sources:Balance:StatementTimeoutMs"] = "1500",
        ["Postgres:Sources:Balance:IdleInTransactionTimeoutMs"] = "5000",
        ["Postgres:Sources:Balance:ReadOnly"] = "true",
        ["Postgres:Sources:Statement:MaxPoolSize"] = "4",
        ["Postgres:Sources:Statement:MinPoolSize"] = "1",
        ["Postgres:Sources:Statement:ConnectionTimeoutSeconds"] = "1",
        ["Postgres:Sources:Statement:CommandTimeoutSeconds"] = "1",
        ["Postgres:Sources:Statement:StatementTimeoutMs"] = "1500",
        ["Postgres:Sources:Statement:IdleInTransactionTimeoutMs"] = "5000",
        ["Postgres:Sources:Statement:ReadOnly"] = "true",
        ["Postgres:Sources:Worker:MaxPoolSize"] = "5",
        ["Postgres:Sources:Worker:MinPoolSize"] = "1",
        ["Postgres:Sources:Worker:ConnectionTimeoutSeconds"] = "5",
        ["Postgres:Sources:Worker:CommandTimeoutSeconds"] = "10",
        ["Postgres:Sources:Worker:LockTimeoutMs"] = "1000",
        ["Postgres:Sources:Worker:StatementTimeoutMs"] = "10000",
        ["Postgres:Sources:Worker:IdleInTransactionTimeoutMs"] = "15000",
        ["Postgres:Sources:Worker:ReadOnly"] = "false"
    };

    private PostgreSqlContainer? _container;
    private PostgresConnectionFactory? _connectionFactory;
    private NpgsqlDataSource? _administrativeSource;

    internal MigrationReport FirstMigration { get; private set; } = new(MigrationStatus.ConnectionFailed, [], null);

    public IReadOnlyDictionary<string, string?> Configuration { get; private set; } = new Dictionary<string, string?>();

    internal PostgresOptions Settings { get; private set; } = new();

    public NpgsqlDataSource AdministrativeSource =>
        _administrativeSource ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

    internal IPostgresConnectionFactory ConnectionFactory =>
        _connectionFactory ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

    public async Task InitializeAsync()
    {
        if (DockerAvailability.SkipReason() is not null)
        {
            return;
        }

        if (!DockerAvailability.IsAvailable)
        {
            throw new InvalidOperationException(DockerAvailability.MissingReason);
        }

        _container = new PostgreSqlBuilder(Image)
            .WithUsername(SuperUser)
            .WithPassword(SuperUserPassword)
            .WithEnvironment("LEDGER_MIGRATOR_PASSWORD", RolePassword)
            .WithEnvironment("LEDGER_API_PASSWORD", RolePassword)
            .WithEnvironment("LEDGER_WORKER_PASSWORD", RolePassword)
            .WithEnvironment("LEDGER_READONLY_PASSWORD", RolePassword)
            .WithCommand("-c", "synchronous_commit=on", "-c", "max_connections=200")
            .WithResourceMapping(ReadInitRolesScript(), InitScriptsDirectory, ExecutableFileMode)
            .Build();

        await _container.StartAsync();

        Configuration = BuildConfiguration(new Dictionary<string, string?>());
        Settings = BindSettings(Configuration);

        _administrativeSource = NpgsqlDataSource.Create(AdministrativeConnectionString(Database));
        _connectionFactory = CreateConnectionFactory(Settings);

        FirstMigration = await CreateMigrationRunner().MigrateAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_connectionFactory is not null)
        {
            await _connectionFactory.DisposeAsync();
        }

        if (_administrativeSource is not null)
        {
            await _administrativeSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    internal MigrationRunner CreateMigrationRunner()
    {
        var factory = _connectionFactory ??
                      throw new InvalidOperationException("The PostgreSQL container is not available.");

        return new MigrationRunner(
            factory,
            Options.Create(Settings),
            Options.Create(new MigrationOptions()),
            TimeProvider.System,
            NullLogger<MigrationRunner>.Instance);
    }

    public IReadOnlyDictionary<string, string?> ConfigurationWith(IReadOnlyDictionary<string, string?> overrides) =>
        BuildConfiguration(overrides);

    internal PostgresOptions SettingsWith(IReadOnlyDictionary<string, string?> overrides) =>
        BindSettings(BuildConfiguration(overrides));

    internal static PostgresConnectionFactory CreateConnectionFactory(PostgresOptions settings)
    {
        var selection = new PostgresSourceSelection(
            [PostgresSource.Write, PostgresSource.Balance, PostgresSource.Statement, PostgresSource.Worker,
                PostgresSource.Migrator]);

        return new PostgresConnectionFactory(Options.Create(settings), selection, NullLoggerFactory.Instance);
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var source = _administrativeSource ??
                     throw new InvalidOperationException("The PostgreSQL container is not available.");

        return await source.OpenConnectionAsync(cancellationToken);
    }

    public async Task<NpgsqlConnection> OpenConnectionAsRoleAsync(string role, CancellationToken cancellationToken)
    {
        var container = _container ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = Database,
            Username = role,
            Password = RolePassword,
            Pooling = false
        };

        var connection = new NpgsqlConnection(builder.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    public async Task<string> ServerLogAsync(CancellationToken cancellationToken)
    {
        var container = _container ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

        var logs = await container.GetLogsAsync(ct: cancellationToken);

        return logs.Stdout + logs.Stderr;
    }

    public async Task<EmptyDatabase> CreateEmptyDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = $"ledger_{Guid.NewGuid():N}";

        await using (var administrative = new NpgsqlConnection(AdministrativeConnectionString("postgres")))
        {
            await administrative.OpenAsync(cancellationToken);
            await ExecuteAsync(administrative, $"CREATE DATABASE {name} OWNER {MigratorRole}", cancellationToken);
            await ExecuteAsync(administrative, $"REVOKE ALL ON DATABASE {name} FROM PUBLIC", cancellationToken);
            await ExecuteAsync(
                administrative,
                $"GRANT CONNECT ON DATABASE {name} TO {ApiRole}, {WorkerRole}, {ReadOnlyRole}",
                cancellationToken);
        }

        await using (var inDatabase = new NpgsqlConnection(AdministrativeConnectionString(name)))
        {
            await inDatabase.OpenAsync(cancellationToken);
            await ExecuteAsync(inDatabase, "REVOKE ALL ON SCHEMA public FROM PUBLIC", cancellationToken);
            await ExecuteAsync(
                inDatabase,
                $"GRANT USAGE ON SCHEMA public TO {ApiRole}, {WorkerRole}, {ReadOnlyRole}",
                cancellationToken);
        }

        var settings = BindSettings(BuildConfiguration(new Dictionary<string, string?> { ["Postgres:Database"] = name }));

        return new EmptyDatabase(name, settings, this);
    }

    internal async Task<NpgsqlConnection> OpenAdministrativeConnectionToAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(AdministrativeConnectionString(database));

        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    internal async Task DropDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        await using var administrative = new NpgsqlConnection(AdministrativeConnectionString("postgres"));

        await administrative.OpenAsync(cancellationToken);
        await ExecuteAsync(administrative, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)", cancellationToken);
    }

    private static byte[] ReadInitRolesScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ??
                   throw new InvalidOperationException($"{SolutionFile} was not found above the test assembly.");
        var content = File.ReadAllText(Path.Combine(root, "docker", "postgres", InitRolesScript));

        return System.Text.Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"));
    }

    private static PostgresOptions BindSettings(IReadOnlyDictionary<string, string?> configuration)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configuration)
            .Build()
            .GetSection(PostgresOptions.SectionName)
            .Get<PostgresOptions>() ?? new PostgresOptions();
    }

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: the SQL text only embeds database names created by this class and role constants.")]
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private Dictionary<string, string?> BuildConfiguration(IReadOnlyDictionary<string, string?> overrides)
    {
        var container = _container ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

        var values = TestConfiguration.ForPostgres(
            container.Hostname,
            container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort),
            Database,
            ApiRole,
            RolePassword);

        values["Postgres:IncludeErrorDetail"] = "false";
        values["Postgres:Sources:Worker:Username"] = WorkerRole;
        values["Postgres:Sources:Migrator:Username"] = MigratorRole;

        foreach (var source in AllSources)
        {
            values[$"Postgres:Sources:{source}:Password"] = RolePassword;
        }

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return values;
    }

    private string AdministrativeConnectionString(string database)
    {
        var container = _container ?? throw new InvalidOperationException("The PostgreSQL container is not available.");

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = database,
            ApplicationName = string.Create(CultureInfo.InvariantCulture, $"integration-tests-{database}")
        }.ConnectionString;
    }
}
