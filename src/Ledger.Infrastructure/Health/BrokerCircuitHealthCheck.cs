using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
