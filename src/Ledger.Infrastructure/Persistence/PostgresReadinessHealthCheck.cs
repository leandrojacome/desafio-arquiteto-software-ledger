using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresReadinessHealthCheck(
    IPostgresConnectionFactory connectionFactory,
    PostgresSource source,
    TimeSpan probeTimeout,
    TimeProvider timeProvider,
    ILogger<PostgresReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(probeTimeout, timeProvider);
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(source, probe.Token);
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT 1", cancellationToken: probe.Token));

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (IsProbeFailure(exception, cancellationToken))
        {
            PersistenceLog.ReadinessProbeFailed(logger, source, exception);

            return HealthCheckResult.Unhealthy("PostgreSQL did not answer a trivial query in time.");
        }
    }

    private static bool IsProbeFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is NpgsqlException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
