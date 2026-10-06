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

internal sealed class PostgresDependencyFailureInspector : IDependencyFailureInspector
{
    private const int SqlStateLength = 5;

    public string? SqlState(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            PostgresException { SqlState.Length: SqlStateLength } postgres => postgres.SqlState,
            PostgresException => null,
            AggregateException aggregate => aggregate.InnerExceptions
                .Select(SqlState)
                .FirstOrDefault(state => state is not null),
            _ => exception.InnerException is { } inner ? SqlState(inner) : null
        };
    }
}

internal static class PostgresSqlStates
{
    public const string IdleInTransactionSessionTimeout = "25P03";
}
