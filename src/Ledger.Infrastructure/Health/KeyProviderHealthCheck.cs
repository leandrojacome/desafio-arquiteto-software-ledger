using Ledger.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Infrastructure.Health;

internal sealed class KeyProviderHealthCheck(IKeyProvider keyProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        if (!keyProvider.IsAvailable)
        {
            return Task.FromResult(HealthCheckResult.Degraded("The key provider does not deliver the active key set."));
        }

        var vanished = keyProvider.VanishedVersions;

        return Task.FromResult(vanished.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded(
                $"Key versions {string.Join(',', vanished)} vanished from the key source since the process started."));
    }
}
