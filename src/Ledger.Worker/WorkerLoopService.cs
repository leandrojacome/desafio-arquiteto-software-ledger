using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Outbox;
using Ledger.Application.Resilience;

namespace Ledger.Worker;

internal abstract class WorkerLoopService : BackgroundService
{
    private readonly WorkerLoop _loop;
    private readonly WorkerLoopHost _host;
    private readonly ExponentialBackoff _failureBackoff;
    private readonly ILogger _logger;

    protected WorkerLoopService(WorkerLoop loop, WorkerLoopHost host, ILogger logger)
    {
        _loop = loop;
        _host = host;
        _logger = logger;
        _failureBackoff = host.NewFailureBackoff();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OnStarted();

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await RunCycleAsync(stoppingToken);

            _host.Heartbeat.Beat(_loop);

            if (!await WorkerDelay.WaitAsync(delay, _host.TimeProvider, stoppingToken))
            {
                break;
            }
        }

        OnStopping();
    }

    protected abstract Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken);

    protected virtual void OnStarted()
    {
    }

    protected virtual void OnStopping()
    {
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Boundary of a background loop: the failure is logged and the loop tries again after a backoff.")]
    private async Task<TimeSpan> RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _host.ScopeFactory.CreateScope();

            var delay = await CycleAsync(scope.ServiceProvider, stoppingToken);

            _failureBackoff.Reset();
            _host.Telemetry.CycleSucceeded(_loop);

            return delay;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return TimeSpan.Zero;
        }
        catch (Exception exception)
        {
            var delay = _failureBackoff.NextDelay();

            _host.Telemetry.CycleFailed(_loop);
            WorkerLog.WorkerLoopFailed(_logger, _loop.Name(), exception.GetType().Name, delay.TotalSeconds);

            return delay;
        }
    }
}
