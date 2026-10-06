using System.Collections.Frozen;
using Npgsql;

namespace Ledger.Infrastructure.Persistence.Retry;

internal static class PostgresRetryPredicate
{
    private const string ConnectionExceptionClass = "08";

    private static readonly FrozenSet<string> RetryableSqlStates = new[]
    {
        PostgresErrorCodes.DeadlockDetected,
        PostgresErrorCodes.SerializationFailure,
        PostgresErrorCodes.AdminShutdown,
        PostgresErrorCodes.CannotConnectNow,
        PostgresSqlStates.IdleInTransactionSessionTimeout
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool ShouldRetry(Exception exception)
    {
        return exception switch
        {
            PostgresException postgres => IsRetryableSqlState(postgres.SqlState),
            NpgsqlException npgsql => npgsql.IsTransient && !ContainsTimeout(npgsql),
            _ => false
        };
    }

    private static bool IsRetryableSqlState(string sqlState) =>
        RetryableSqlStates.Contains(sqlState)
        || sqlState.StartsWith(ConnectionExceptionClass, StringComparison.Ordinal);

    private static bool ContainsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
