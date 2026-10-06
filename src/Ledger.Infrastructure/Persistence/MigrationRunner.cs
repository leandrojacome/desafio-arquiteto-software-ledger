using DbUp;
using Ledger.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed partial class MigrationRunner(
    IPostgresConnectionFactory connectionFactory,
    IOptions<PostgresOptions> options,
    IOptions<MigrationOptions> migrationOptions,
    TimeProvider timeProvider,
    ILogger<MigrationRunner> logger) : IDatabaseMigrator
{
    internal const long AdvisoryLockKey = 727001;
    private const string ScriptSuffix = ".sql";
    private const string MigrationsNamespace = "Ledger.Infrastructure.Persistence.Migrations.";

    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(250);

    public async Task<MigrationReport> MigrateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await MigrateUnderLockAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            return ConnectionFailure(exception);
        }
        catch (TimeoutException exception)
        {
            return ConnectionFailure(exception);
        }
    }

    async Task<MigrationStatus> IDatabaseMigrator.MigrateAsync(CancellationToken cancellationToken)
    {
        var report = await MigrateAsync(cancellationToken);

        return report.Status;
    }

    private static bool IsMigrationScript(string resourceName)
    {
        return resourceName.StartsWith(MigrationsNamespace, StringComparison.Ordinal)
               && resourceName.EndsWith(ScriptSuffix, StringComparison.Ordinal);
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task UnlockAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private async Task<bool> AcquireLockAsync(
        NpgsqlConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();

        while (true)
        {
            if (await TryLockAsync(connection, cancellationToken))
            {
                return true;
            }

            var remaining = timeout - timeProvider.GetElapsedTime(started);

            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(remaining < LockPollInterval ? remaining : LockPollInterval, timeProvider,
                cancellationToken);
        }
    }

    private async Task ReleaseLockAsync(NpgsqlConnection connection)
    {
        try
        {
            await UnlockAsync(connection);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            LogMigrationLockReleaseFailed(logger, exception);
        }
    }

    private async Task<MigrationReport> MigrateUnderLockAsync(CancellationToken cancellationToken)
    {
        var lockTimeoutSeconds = migrationOptions.Value.LockTimeoutSeconds;

        await using var lockConnection =
            await connectionFactory.OpenConnectionAsync(PostgresSource.Migrator, cancellationToken);

        if (!await AcquireLockAsync(lockConnection, TimeSpan.FromSeconds(lockTimeoutSeconds), cancellationToken))
        {
            LogMigrationLockTimeout(logger, lockTimeoutSeconds);

            return new(MigrationStatus.LockTimedOut, [], null);
        }

        try
        {
            LogMigrationStarted(logger);

            var report = await Task.Run(Upgrade, cancellationToken);

            var level = report.Status == MigrationStatus.Succeeded ? LogLevel.Information : LogLevel.Error;

            LogMigrationFinished(logger, level, report.Status, report.AppliedScripts.Count);

            return report;
        }
        finally
        {
            await ReleaseLockAsync(lockConnection);
        }
    }

    private static string Describe(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ");

        var cause = exception.InnerException?.Message.ReplaceLineEndings(" ");

        return cause is null || string.Equals(cause, message, StringComparison.Ordinal)
            ? message
            : $"{message} ({cause})";
    }

    private MigrationReport ConnectionFailure(Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Critical))
        {
            var reason = Describe(exception);

            LogMigrationUnreachable(logger, reason);
        }

        LogMigrationUnreachableDetail(logger, exception);

        return new(MigrationStatus.ConnectionFailed, [], exception);
    }

    private MigrationReport Upgrade()
    {
        var engine = DeployChanges.To
            .PostgresqlDatabase(PostgresConnectionString.Build(options.Value, PostgresSource.Migrator))
            .WithScriptsEmbeddedInAssembly(typeof(MigrationRunner).Assembly, IsMigrationScript)
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogTo(new LoggerUpgradeLog(logger))
            .Build();

        var result = engine.PerformUpgrade();

        var applied = result.Scripts.Select(script => script.Name).ToList();
        var status = result.Successful ? MigrationStatus.Succeeded : MigrationStatus.ScriptFailed;

        return new(status, applied, result.Error);
    }

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information, Message = "Applying database migrations")]
    private static partial void LogMigrationStarted(ILogger logger);

    [LoggerMessage(EventId = 5011,
        Message = "Database migrations finished with status {Status}. Scripts applied: {AppliedCount}")]
    private static partial void LogMigrationFinished(
        ILogger logger,
        LogLevel level,
        MigrationStatus status,
        int appliedCount);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Critical,
        Message = "Could not reach the database to apply migrations: {Reason:l}")]
    private static partial void LogMigrationUnreachable(ILogger logger, string reason);

    [LoggerMessage(EventId = 5015, Level = LogLevel.Debug,
        Message = "Details of the connection failure")]
    private static partial void LogMigrationUnreachableDetail(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5013, Level = LogLevel.Critical,
        Message = "The migration lock stayed taken by another session for more than {TimeoutSeconds} seconds")]
    private static partial void LogMigrationLockTimeout(ILogger logger, int timeoutSeconds);

    [LoggerMessage(EventId = 5014, Level = LogLevel.Warning,
        Message = "The migration lock could not be released explicitly. Closing the connection releases it")]
    private static partial void LogMigrationLockReleaseFailed(ILogger logger, Exception exception);
}
