using Ledger.Application.Abstractions;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

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
