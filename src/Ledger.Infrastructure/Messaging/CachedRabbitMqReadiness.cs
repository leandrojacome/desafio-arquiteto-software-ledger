using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class CachedRabbitMqReadiness : IHealthCheck
{
    private readonly CachedHealthCheck _cached;

    public CachedRabbitMqReadiness(
        RabbitMqReadinessHealthCheck probe,
        IOptions<ResilienceOptions> resilience,
        TimeProvider timeProvider)
    {
        _cached = new CachedHealthCheck(
            probe,
            TimeSpan.FromSeconds(resilience.Value.Health.CacheSeconds),
            timeProvider);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return _cached.CheckHealthAsync(context, cancellationToken);
    }
}
