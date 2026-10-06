using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal sealed class RabbitMqReadinessHealthCheck(
    IRabbitMqConnector connector,
    IOptions<RabbitMqOptions> options,
    TimeProvider timeProvider,
    ILogger<RabbitMqReadinessHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.Value.ConnectTimeoutSeconds),
            timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await connector.ConnectAsync(linked.Token);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (BrokerFailures.IsAttemptFailure(exception, cancellationToken))
        {
            MessagingLog.BrokerProbeDegraded(logger, exception.GetType().Name);

            return HealthCheckResult.Degraded("The broker did not accept a connection in time.");
        }
    }
}

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
