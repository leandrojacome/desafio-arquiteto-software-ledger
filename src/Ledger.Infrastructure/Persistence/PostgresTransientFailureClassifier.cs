using Ledger.Application.Abstractions;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresTransientFailureClassifier : ITransientFailureClassifier
{
    private static readonly HashSet<string> TransientSqlStates = new(StringComparer.Ordinal)
    {
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.LockNotAvailable,
        PostgresErrorCodes.QueryCanceled,
        PostgresErrorCodes.TooManyConnections,
        PostgresErrorCodes.AdminShutdown,
        PostgresErrorCodes.CannotConnectNow,
        PostgresErrorCodes.DiskFull,
        PostgresErrorCodes.ReadOnlySqlTransaction,
        PostgresErrorCodes.CrashShutdown,
        PostgresSqlStates.IdleInTransactionSessionTimeout
    };

    public bool IsTransient(Exception exception)
    {
        return exception switch
        {
            PostgresException postgres => TransientSqlStates.Contains(postgres.SqlState) || postgres.IsTransient,
            NpgsqlException npgsql => npgsql.IsTransient,
            TimeoutException => true,
            _ => exception.InnerException is { } inner && IsTransient(inner)
        };
    }
}
