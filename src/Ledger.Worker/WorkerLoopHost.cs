using Ledger.Application.Abstractions;
using Ledger.Application.Resilience;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class WorkerLoopHost(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IWorkerHeartbeat heartbeat,
    IWorkerLoopTelemetry telemetry,
    IOptions<WorkerOptions> options)
{
    private const int FailureJitterPercent = 20;

    public IServiceScopeFactory ScopeFactory { get; } = scopeFactory;

    public TimeProvider TimeProvider { get; } = timeProvider;

    public IWorkerHeartbeat Heartbeat { get; } = heartbeat;

    public IWorkerLoopTelemetry Telemetry { get; } = telemetry;

    public ExponentialBackoff NewFailureBackoff()
    {
        var backoff = options.Value.FailureBackoff;

        return new ExponentialBackoff(
            TimeSpan.FromSeconds(backoff.MinSeconds),
            TimeSpan.FromSeconds(backoff.MaxSeconds),
            FailureJitterPercent);
    }
}
