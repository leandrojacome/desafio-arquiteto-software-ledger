using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Health;

internal sealed class OutboxLagHealthCheck(
    OutboxStatsHolder stats,
    IOptions<WorkerHealthOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    private static readonly TimeSpan FirstMeasurementGrace = TimeSpan.FromSeconds(60);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        if (stats.MeasuredAt is not { } measuredAt)
        {
            var neverMeasured = now - stats.StartedAt > FirstMeasurementGrace;

            return Task.FromResult(neverMeasured
                ? HealthCheckResult.Degraded("The outbox has not been measured since the worker started.")
                : HealthCheckResult.Healthy());
        }

        if (now - measuredAt > stats.StaleAfter)
        {
            return Task.FromResult(HealthCheckResult.Degraded("The last outbox measurement is too old to be trusted."));
        }

        var limit = options.Value.OutboxLagSeconds;

        var result = stats.Snapshot is { OldestPendingAgeSeconds: { } age } && age > limit
            ? HealthCheckResult.Degraded("The oldest pending outbox message is older than the allowed lag.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
