using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Infrastructure.Health;

internal sealed class CachedHealthCheck(IHealthCheck inner, TimeSpan window, TimeProvider timeProvider) : IHealthCheck
{
    private readonly object _gate = new();

    private CachedResult? _cached;
    private Task<HealthCheckResult>? _inFlight;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        Task<HealthCheckResult> probe;

        lock (_gate)
        {
            if (_cached is { } cached && timeProvider.GetUtcNow() < cached.ExpiresAt)
            {
                return Task.FromResult(cached.Result);
            }

            _inFlight ??= ProbeAsync(context);
            probe = _inFlight;
        }

        return probe.WaitAsync(cancellationToken);
    }

    private async Task<HealthCheckResult> ProbeAsync(HealthCheckContext context)
    {
        await Task.Yield();

        try
        {
            var result = await inner.CheckHealthAsync(context, CancellationToken.None);

            lock (_gate)
            {
                _cached = new CachedResult(result, timeProvider.GetUtcNow() + window);
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = null;
            }
        }
    }

    private sealed record CachedResult(HealthCheckResult Result, DateTimeOffset ExpiresAt);
}
