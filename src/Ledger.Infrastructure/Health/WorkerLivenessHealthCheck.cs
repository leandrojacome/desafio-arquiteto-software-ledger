using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Health;

internal sealed class WorkerLivenessHealthCheck(
    IWorkerHeartbeat heartbeat,
    IOptions<WorkerHealthOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();

        var integrityLimit = TimeSpan.FromMinutes(settings.IntegrityHeartbeatMinutes);

        var stalled = new List<string>(3);

        foreach (var (loop, limit) in new[]
                 {
                     (WorkerLoop.Outbox, TimeSpan.FromSeconds(settings.OutboxHeartbeatSeconds)),
                     (WorkerLoop.IntegrityRecent, integrityLimit),
                     (WorkerLoop.IntegrityFull, integrityLimit)
                 })
        {
            if (IsStalled(loop, limit, now))
            {
                stalled.Add(loop.Name());
            }
        }

        var result = stalled.Count == 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"No cycle completed in time for: {string.Join(", ", stalled)}.");

        return Task.FromResult(result);
    }

    private bool IsStalled(WorkerLoop loop, TimeSpan limit, DateTimeOffset now)
    {
        return heartbeat.LastBeat(loop) is { } lastBeat && now - lastBeat > limit;
    }
}
