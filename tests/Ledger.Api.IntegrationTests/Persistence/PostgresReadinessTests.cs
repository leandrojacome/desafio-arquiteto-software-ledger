using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresReadinessTests(PostgresFixture postgres)
{
    private const string PostgresCheck = "postgres";
    private const string SchemaCheck = "schema";
    private const int ProbeBudgetMilliseconds = 2500;
    private const int ChecksPerWindow = 2;

    private static readonly Dictionary<string, string?> Unreachable = new()
    {
        ["Postgres:Host"] = "127.0.0.1",
        ["Postgres:Port"] = "1"
    };

    [DockerFact]
    public async Task Readiness_WithTheDatabaseUpAndTheSchemaCurrent_IsHealthy()
    {
        await using var host = Build(new Dictionary<string, string?>());

        var report = await host.ReadyAsync();

        report.Status.ShouldBe(HealthStatus.Healthy);
        report.Entries.Keys.ShouldBe([PostgresCheck, SchemaCheck], ignoreOrder: true);
    }

    [DockerFact]
    public async Task Readiness_ProbesTheStatementSource()
    {
        await using var host = Build(new Dictionary<string, string?>());

        await host.ReadyAsync();

        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = 'ledger-api-statement'");

        ((long)(await command.ExecuteScalarAsync() ?? 0L)).ShouldBeGreaterThan(0);
        host.Opened.ShouldBe(new Dictionary<PostgresSource, int> { [PostgresSource.Statement] = ChecksPerWindow });
    }

    [DockerFact]
    public async Task Readiness_WithAnUnmigratedDatabase_IsUnhealthyBecauseTheSchemaIsMissing_AndRecoversAfterTheMigration()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var host = Build(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });

        var before = await host.ReadyAsync();

        before.Status.ShouldBe(HealthStatus.Unhealthy);
        before.Entries[PostgresCheck].Status.ShouldBe(HealthStatus.Healthy);
        before.Entries[SchemaCheck].Status.ShouldBe(HealthStatus.Unhealthy);
        host.Logs.Events.Count(log => log.EventId == 9102).ShouldBe(1);

        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

        (await host.ReadyAsync()).Status.ShouldBe(HealthStatus.Unhealthy);

        host.Time.Advance(TimeSpan.FromSeconds(5));

        (await host.ReadyAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [DockerFact]
    public async Task Readiness_WhenTheJournalIsBehindTheCode_IsUnhealthyAndLogsEvent9102WithoutSensitiveData()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

        var remaining = new List<string>();

        await using (var administrative = await database.OpenAdministrativeConnectionAsync(CancellationToken.None))
        {
            await using (var rewind = new NpgsqlCommand(
                             "DELETE FROM schemaversions WHERE scriptname = (SELECT max(scriptname) FROM schemaversions)",
                             administrative))
            {
                await rewind.ExecuteNonQueryAsync();
            }

            await using var read = new NpgsqlCommand("SELECT scriptname FROM schemaversions", administrative);
            await using var reader = await read.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                remaining.Add(reader.GetString(0));
            }
        }

        await using var host = Build(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });

        var report = await host.ReadyAsync();

        var logged = host.Logs.Events.Single(log => log.EventId == 9102);

        report.Entries[SchemaCheck].Status.ShouldBe(HealthStatus.Unhealthy);
        report.Entries[PostgresCheck].Status.ShouldBe(HealthStatus.Healthy);
        logged.Level.ShouldBe(LogLevel.Warning);
        logged.Properties["CurrentVersion"].ShouldBe(SchemaVersion.Highest(remaining));
        logged.Properties["ExpectedVersion"].ShouldBe(SchemaVersion.Expected);
        logged.Message.ShouldNotContain("Password");
    }

    [DockerFact]
    public async Task Readiness_WhenTheJournalIsAheadOfTheCode_StaysHealthy()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);

        (await database.MigrateAsync(CancellationToken.None)).Succeeded.ShouldBeTrue();

        await using (var administrative = await database.OpenAdministrativeConnectionAsync(CancellationToken.None))
        await using (var advance = new NpgsqlCommand(
                         "INSERT INTO schemaversions (scriptname, applied) VALUES ('Ledger.Infrastructure.Persistence.Migrations.9999_create_future_thing.sql', now())",
                         administrative))
        {
            await advance.ExecuteNonQueryAsync();
        }

        await using var host = Build(new Dictionary<string, string?> { ["Postgres:Database"] = database.Name });

        (await host.ReadyAsync()).Status.ShouldBe(HealthStatus.Healthy);
    }

    [DockerFact]
    public async Task Readiness_WithTheDatabaseUnreachable_IsUnhealthyWithinTheProbeTimeout_AndLogsEvent9101()
    {
        await using var host = Build(Unreachable);
        var watch = Stopwatch.StartNew();

        var report = await host.ReadyAsync();

        report.Status.ShouldBe(HealthStatus.Unhealthy);
        watch.ElapsedMilliseconds.ShouldBeLessThan(ProbeBudgetMilliseconds);
        host.Logs.Events.Count(log => log.EventId == 9101).ShouldBeGreaterThan(0);
    }

    [DockerFact]
    public async Task Readiness_FiftyConcurrentCalls_ShareASingleProbePerWindow()
    {
        await using var host = Build(new Dictionary<string, string?>());

        await host.ReadyAsync();
        host.Opened[PostgresSource.Statement].ShouldBe(ChecksPerWindow);

        var cached = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => host.ReadyAsync()));

        cached.ShouldAllBe(report => report.Status == HealthStatus.Healthy);
        host.Opened[PostgresSource.Statement].ShouldBe(ChecksPerWindow);

        host.Time.Advance(TimeSpan.FromSeconds(5));

        var refreshed = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => host.ReadyAsync()));

        refreshed.ShouldAllBe(report => report.Status == HealthStatus.Healthy);
        host.Opened[PostgresSource.Statement].ShouldBe(2 * ChecksPerWindow);
    }

    [DockerFact]
    public async Task Warmup_OpensTheMinimumPoolOfEachSource()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Postgres:Sources:Write:MinPoolSize"] = "3",
            ["Postgres:Sources:Statement:MinPoolSize"] = "2"
        };
        await using var host = Build(overrides, PostgresSource.Write, PostgresSource.Statement);

        foreach (var service in host.Provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }

        (await IdleSessionsAsync("ledger-api-write")).ShouldBeGreaterThanOrEqualTo(3);
        (await IdleSessionsAsync("ledger-api-statement")).ShouldBeGreaterThanOrEqualTo(2);
    }

    [DockerFact]
    public async Task Warmup_WithTheDatabaseUnreachable_DoesNotStopTheStartup_AndLogsEvent5031()
    {
        var overrides = new Dictionary<string, string?>(Unreachable) { ["Postgres:Sources:Statement:MinPoolSize"] = "1" };
        await using var host = Build(overrides);

        foreach (var service in host.Provider.GetServices<IHostedService>())
        {
            await service.StartAsync(CancellationToken.None);
        }

        host.Logs.Events.Count(log => log.EventId == 5031).ShouldBeGreaterThan(0);
    }

    [DockerFact]
    public async Task Registration_ExposesThePersistencePorts()
    {
        await using var host = Build(new Dictionary<string, string?>());

        using var scope = host.Provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IIdGenerator>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IBalanceReader>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IStatementReader>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<ITransientFailureClassifier>().ShouldNotBeNull();
    }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The logger factory of the provider disposes the capturing provider.")]
    private ReadinessHost Build(Dictionary<string, string?> overrides, params PostgresSource[] sources)
    {
        var time = new FakeTimeProvider();
        var logs = new CapturingLoggerProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(postgres.ConfigurationWith(overrides)).Build();
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Development));
        services.AddSingleton<IWorkerHeartbeat>(provider => new WorkerHeartbeat(provider.GetRequiredService<TimeProvider>()));
        services.AddHealthChecks();
        services.AddLedgerPostgres(configuration, sources.Length == 0 ? [PostgresSource.Statement] : sources);
        services.AddLedgerWriteStore();
        services.AddLedgerReaders();
        services.AddLedgerObservability();
        services.AddSingleton<IPostgresConnectionFactory>(provider =>
            new CountingConnectionFactory(provider.GetRequiredService<PostgresConnectionFactory>()));

        return new ReadinessHost(services.BuildServiceProvider(), time, logs);
    }

    private async Task<long> IdleSessionsAsync(string application)
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE application_name = @application AND state = 'idle'");

        command.Parameters.AddWithValue("application", application);

        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private sealed class CountingConnectionFactory(PostgresConnectionFactory inner) : IPostgresConnectionFactory
    {
        private readonly Dictionary<PostgresSource, int> _opened = [];

        public IReadOnlyDictionary<PostgresSource, int> Opened
        {
            get
            {
                lock (_opened)
                {
                    return new Dictionary<PostgresSource, int>(_opened);
                }
            }
        }

        public ValueTask<NpgsqlConnection> OpenConnectionAsync(PostgresSource source, CancellationToken cancellationToken)
        {
            lock (_opened)
            {
                _opened[source] = _opened.GetValueOrDefault(source) + 1;
            }

            return inner.OpenConnectionAsync(source, cancellationToken);
        }
    }

    private sealed class ReadinessHost(ServiceProvider provider, FakeTimeProvider time, CapturingLoggerProvider logs)
        : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;

        public FakeTimeProvider Time => time;

        public CapturingLoggerProvider Logs => logs;

        public IReadOnlyDictionary<PostgresSource, int> Opened =>
            ((CountingConnectionFactory)provider.GetRequiredService<IPostgresConnectionFactory>()).Opened;

        public async Task<HealthReport> ReadyAsync()
        {
            var service = provider.GetRequiredService<HealthCheckService>();

            return await service.CheckHealthAsync(
                registration => registration.Tags.Contains(HealthCheckTags.Ready),
                CancellationToken.None);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
