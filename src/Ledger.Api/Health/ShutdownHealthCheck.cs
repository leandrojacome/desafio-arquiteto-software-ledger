using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Api.Health;

internal sealed class ShutdownHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = lifetime.ApplicationStopping.IsCancellationRequested
            ? HealthCheckResult.Unhealthy("The host has been asked to stop.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
