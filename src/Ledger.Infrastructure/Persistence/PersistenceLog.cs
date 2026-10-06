using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Persistence;

internal static partial class PersistenceLog
{
    public const string NoSqlState = "none";

    [LoggerMessage(EventId = 1006, EventName = "TransientDatabaseFailure", Level = LogLevel.Warning,
        Message = "Transient database failure with SQLSTATE {SqlState}. The write transaction failed on attempt {Attempt} and will run again")]
    public static partial void TransientDatabaseFailure(ILogger logger, string sqlState, int attempt);

    [LoggerMessage(EventId = 1007, EventName = "LockTimeoutExceeded", Level = LogLevel.Warning,
        Message = "The account row stayed locked beyond the lock timeout")]
    public static partial void LockTimeoutExceeded(ILogger logger);

    [LoggerMessage(EventId = 1008, EventName = "UnexpectedConstraintViolation", Level = LogLevel.Error,
        Message = "The database refused the write with SQLSTATE {SqlState} on constraint {ConstraintName}, which the design considers impossible")]
    public static partial void UnexpectedConstraintViolation(ILogger logger, string sqlState, string constraintName);

    [LoggerMessage(EventId = 1009, EventName = "RollbackFailed", Level = LogLevel.Warning,
        Message = "The rollback of a write transaction did not finish. The database will discard the session")]
    public static partial void RollbackFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 5030, EventName = "ConnectionPoolWarmedUp", Level = LogLevel.Debug,
        Message = "Connection pool {Source} warmed up with {Connections} connections")]
    public static partial void ConnectionPoolWarmedUp(ILogger logger, PostgresSource source, int connections);

    [LoggerMessage(EventId = 5031, EventName = "ConnectionPoolWarmupFailed", Level = LogLevel.Warning,
        Message = "Connection pool {Source} could not be warmed up. The first requests will open the connections")]
    public static partial void ConnectionPoolWarmupFailed(ILogger logger, PostgresSource source, Exception exception);

    [LoggerMessage(EventId = 9101, EventName = "ReadinessProbeFailed", Level = LogLevel.Warning,
        Message = "The database readiness probe on source {Source} failed or ran out of time")]
    public static partial void ReadinessProbeFailed(ILogger logger, PostgresSource source, Exception exception);

    [LoggerMessage(EventId = 9102, EventName = "SchemaBehindCode", Level = LogLevel.Warning,
        Message = "The database schema is at version {CurrentVersion} and the code expects {ExpectedVersion}")]
    public static partial void SchemaBehindCode(ILogger logger, int currentVersion, int expectedVersion);
}
