using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ledger.Infrastructure.Persistence;

internal sealed class SchemaVersionHealthCheck(
    SchemaVersionReader reader,
    PostgresSource source,
    TimeSpan probeTimeout,
    TimeProvider timeProvider,
    ILogger<SchemaVersionHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(probeTimeout, timeProvider);
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var current = await reader.ReadCurrentAsync(source, probe.Token);

            if (SchemaVersion.IsSatisfiedBy(SchemaVersion.Expected, current))
            {
                return HealthCheckResult.Healthy();
            }

            PersistenceLog.SchemaBehindCode(logger, current ?? 0, SchemaVersion.Expected);

            return HealthCheckResult.Unhealthy("The database schema is behind the code.");
        }
        catch (Exception exception) when (IsProbeFailure(exception, cancellationToken))
        {
            PersistenceLog.ReadinessProbeFailed(logger, source, exception);

            return HealthCheckResult.Unhealthy("PostgreSQL did not answer the schema version query in time.");
        }
    }

    private static bool IsProbeFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is NpgsqlException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
