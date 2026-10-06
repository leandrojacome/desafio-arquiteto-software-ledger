using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Ledger.Infrastructure.Health;

internal sealed class BrokerCircuitHealthCheck(IEventPublisher publisher) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = publisher.Circuit == BrokerCircuitState.Closed
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("The broker circuit is not closed.");

        return Task.FromResult(result);
    }
}

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
