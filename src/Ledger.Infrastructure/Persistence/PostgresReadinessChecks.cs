using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal sealed class PostgresReadinessChecks
{
    public PostgresReadinessChecks(
        IPostgresConnectionFactory connectionFactory,
        SchemaVersionReader schemaReader,
        PostgresSourceSelection selection,
        IOptions<ResilienceOptions> options,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        var health = options.Value.Health;
        var probeTimeout = TimeSpan.FromSeconds(health.ProbeTimeoutSeconds);
        var window = TimeSpan.FromSeconds(health.CacheSeconds);

        Postgres = new CachedHealthCheck(
            new PostgresReadinessHealthCheck(
                connectionFactory,
                selection.ReadinessSource,
                probeTimeout,
                timeProvider,
                loggerFactory.CreateLogger<PostgresReadinessHealthCheck>()),
            window,
            timeProvider);

        Schema = new CachedHealthCheck(
            new SchemaVersionHealthCheck(
                schemaReader,
                selection.ReadinessSource,
                probeTimeout,
                timeProvider,
                loggerFactory.CreateLogger<SchemaVersionHealthCheck>()),
            window,
            timeProvider);
    }

    public IHealthCheck Postgres { get; }

    public IHealthCheck Schema { get; }
}
