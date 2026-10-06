using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Infrastructure.Health;

internal sealed class KeyUsageHealthCheck(KeyUsageHolder usage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = usage.Anomalous
            ? HealthCheckResult.Degraded("Accounts are sealed with key versions this process cannot read or that are newer than its active version.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}
