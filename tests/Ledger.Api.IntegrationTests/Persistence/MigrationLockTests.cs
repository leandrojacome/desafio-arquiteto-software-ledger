using System.Diagnostics;
using System.Globalization;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class MigrationLockTests(PostgresFixture postgres)
{
    private const int MigrationUnreachableEventId = 5012;
    private const int MigrationLockTimeoutEventId = 5013;
    private const int MigrationLockReleaseFailedEventId = 5014;
    private const int MigrationUnreachableDetailEventId = 5015;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task TwoRunsStartedWhileAnotherSessionHoldsTheLock_BothWait_AndThenExactlyOneAppliesTheScripts()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var holder = await MigrationLockHolder.HoldAsync(database);

        var first = database.MigrateAsync(CancellationToken.None);
        var second = database.MigrateAsync(CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await MigrationLockHolder.CountMigratorSessionsAsync(database) >= 2,
            Patience,
            "both runs connected and polling for the migration lock");

        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();
        (await MigrationLockHolder.CountPublicTablesAsync(database)).ShouldBe(0);

        await MigrationLockHolder.ReleaseAsync(holder);

        var reports = await Task.WhenAll(first, second).WaitAsync(Patience);

        reports.ShouldAllBe(report => report.Status == MigrationStatus.Succeeded);
        reports.Select(report => report.AppliedScripts.Count).Order().ToArray()
            .ShouldBe(new[] { 0, postgres.FirstMigration.AppliedScripts.Count });
    }

    [DockerFact]
    public async Task ALockHeldPastTheTimeout_EndsWithLockTimedOut_LogsEvent5013_AppliesNothing_AndLeavesTheNextRunFree()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var holder = await MigrationLockHolder.HoldAsync(database);
        var logger = new LogCapture<MigrationRunner>();
        var stopwatch = Stopwatch.StartNew();

        var report = await database
            .MigrateAsync(new MigrationOptions { LockTimeoutSeconds = 1 }, logger, CancellationToken.None)
            .WaitAsync(Patience);

        stopwatch.Stop();

        report.Status.ShouldBe(MigrationStatus.LockTimedOut);
        report.Succeeded.ShouldBeFalse();
        report.AppliedScripts.ShouldBeEmpty();
        report.Error.ShouldBeNull();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));

        var timeout = logger.Events.Single(captured => captured.Id == MigrationLockTimeoutEventId);

        timeout.Level.ShouldBe(LogLevel.Critical);
        timeout.Properties["TimeoutSeconds"].ShouldBe(1);
        (await MigrationLockHolder.CountPublicTablesAsync(database)).ShouldBe(0);

        await MigrationLockHolder.ReleaseAsync(holder);

        var next = await database.MigrateAsync(CancellationToken.None);

        next.Status.ShouldBe(MigrationStatus.Succeeded);
        next.AppliedScripts.Count.ShouldBe(postgres.FirstMigration.AppliedScripts.Count);
    }

    [DockerFact]
    public async Task ALockReleasedAfterSomeTime_IsTakenByTheWaitingRunWithoutWaitingForTheWholeTimeout()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var holder = await MigrationLockHolder.HoldAsync(database);
        var logger = new LogCapture<MigrationRunner>();

        var waiting = database.MigrateAsync(new MigrationOptions { LockTimeoutSeconds = 3600 }, logger,
            CancellationToken.None);

        await ConditionWait.UntilAsync(
            async () => await MigrationLockHolder.CountMigratorSessionsAsync(database) >= 1,
            Patience,
            "the run connected and polling for the migration lock");

        waiting.IsCompleted.ShouldBeFalse();

        await MigrationLockHolder.ReleaseAsync(holder);

        var report = await waiting.WaitAsync(TimeSpan.FromSeconds(30));

        report.Status.ShouldBe(MigrationStatus.Succeeded);
        logger.Events.ShouldNotContain(captured => captured.Id == MigrationLockTimeoutEventId);
    }

    [DockerFact]
    public async Task AFailureToReleaseTheLock_IsLoggedAsAWarning_AndDoesNotChangeTheReportOfTheMigration()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        await using var administrative = await database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        var logger = new KillLockSessionWhenTheScriptsStartLogger(administrative, database.Name);

        var report = await database
            .MigrateAsync(new MigrationOptions(), logger, CancellationToken.None)
            .WaitAsync(Patience);

        logger.TerminatedSessions.ShouldBe(1);
        report.Status.ShouldBe(MigrationStatus.Succeeded);
        report.AppliedScripts.Count.ShouldBe(postgres.FirstMigration.AppliedScripts.Count);

        var warning = logger.Events.Single(captured => captured.Id == MigrationLockReleaseFailedEventId);

        warning.Level.ShouldBe(LogLevel.Warning);
        (await database.MigrateAsync(CancellationToken.None)).AppliedScripts.ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ADatabaseThatCannotBeReached_EndsWithConnectionFailed_AndLogsEvent5012AsOneLineWithTheReason()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        var unreachable = new PostgresOptions
        {
            Host = database.Settings.Host,
            Port = 1,
            Database = database.Settings.Database,
            SslMode = database.Settings.SslMode,
            Sources = database.Settings.Sources
        };
        var logger = new LogCapture<MigrationRunner>();
        await using var factory = PostgresFixture.CreateConnectionFactory(unreachable);
        var runner = new MigrationRunner(
            factory,
            Options.Create(unreachable),
            Options.Create(new MigrationOptions()),
            TimeProvider.System,
            logger);

        var report = await runner.MigrateAsync(CancellationToken.None).WaitAsync(Patience);

        report.Status.ShouldBe(MigrationStatus.ConnectionFailed);

        var failure = logger.Events.Single(captured => captured.Id == MigrationUnreachableEventId);

        failure.Level.ShouldBe(LogLevel.Critical);
        failure.Message.ShouldStartWith("Could not reach the database to apply migrations: ");
        failure.Message.ShouldNotContain('\n');
        logger.Events.Single(captured => captured.Id == MigrationUnreachableDetailEventId).Level.ShouldBe(LogLevel.Debug);
    }

    private sealed class KillLockSessionWhenTheScriptsStartLogger(NpgsqlConnection administrative, string databaseName)
        : ILogger<MigrationRunner>
    {
        private const int MigrationStartedEventId = 5010;

        private readonly LogCapture<MigrationRunner> _captured = new();

        public IReadOnlyList<CapturedEvent> Events => _captured.Events;

        public int TerminatedSessions { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _captured.Log(logLevel, eventId, state, exception, formatter);

            if (eventId.Id == MigrationStartedEventId)
            {
                TerminatedSessions += TerminateTheSessionHoldingTheLock();
            }
        }

        private int TerminateTheSessionHoldingTheLock()
        {
            using var command = new NpgsqlCommand(
                """
                SELECT count(pg_terminate_backend(l.pid))
                FROM pg_locks l
                JOIN pg_database d ON d.oid = l.database
                WHERE l.locktype = 'advisory'
                  AND l.granted
                  AND l.objsubid = 1
                  AND l.classid::bigint = 0
                  AND l.objid::bigint = @key
                  AND d.datname = @database
                """,
                administrative);

            command.Parameters.AddWithValue("key", MigrationRunner.AdvisoryLockKey);
            command.Parameters.AddWithValue("database", databaseName);

            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }
}
