using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed partial class IntegrityCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrityOptions _options;
    private readonly IWorkerHeartbeat _heartbeat;
    private readonly IWorkerLoopTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrityCheckService> _logger;

    private readonly TaskCompletionSource _firstRecentRunFinished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IntegrityCheckService(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegrityOptions> options,
        IWorkerHeartbeat heartbeat,
        IWorkerLoopTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<IntegrityCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _heartbeat = heartbeat;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private TimeSpan RecentInterval => TimeSpan.FromMinutes(_options.RecentIntervalMinutes);

    private TimeSpan RecentOverlap => TimeSpan.FromMinutes(_options.RecentOverlapMinutes);

    private TimeSpan FullInterval => TimeSpan.FromHours(_options.FullIntervalHours);

    private TimeSpan FullLookback => FullInterval + RecentOverlap;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_logger);

        await Task.WhenAll(RunRecentLoopAsync(stoppingToken), RunFullLoopAsync(stoppingToken));

        LogStopping(_logger);
    }

    private async Task RunRecentLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await RunOnceAsync(IntegrityMode.Recent, stoppingToken);

            Report(WorkerLoop.IntegrityRecent, result);
            _firstRecentRunFinished.TrySetResult();
            _heartbeat.Beat(WorkerLoop.IntegrityRecent);

            if (!await WorkerDelay.WaitAsync(RecentInterval, _timeProvider, stoppingToken))
            {
                break;
            }
        }

        _firstRecentRunFinished.TrySetResult();
    }

    private async Task RunFullLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _firstRecentRunFinished.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var (delay, result) = await RunFullWhenDueAsync(stoppingToken);

            Report(WorkerLoop.IntegrityFull, result);
            _heartbeat.Beat(WorkerLoop.IntegrityFull);

            if (!await WorkerDelay.WaitAsync(delay, _timeProvider, stoppingToken))
            {
                break;
            }
        }
    }

    private void Report(WorkerLoop loop, RunResult result)
    {
        switch (result)
        {
            case RunResult.Completed:
                _telemetry.CycleSucceeded(loop);
                break;
            case RunResult.Failed:
                _telemetry.CycleFailed(loop);
                break;
            case RunResult.Cancelled:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown run result.");
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Boundary of a background loop: the failure is logged and the next run tries again.")]
    private async Task<(TimeSpan Delay, RunResult Result)> RunFullWhenDueAsync(CancellationToken stoppingToken)
    {
        try
        {
            var remaining = await TimeUntilFullRunAsync(stoppingToken);

            if (remaining > TimeSpan.Zero)
            {
                return (remaining < RecentInterval ? remaining : RecentInterval, RunResult.Completed);
            }

            var result = await RunOnceAsync(IntegrityMode.Full, stoppingToken);

            return (RecentInterval, result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return (TimeSpan.Zero, RunResult.Cancelled);
        }
        catch (Exception exception)
        {
            WorkerLog.IntegrityRunFailed(_logger, IntegrityMode.Full.AuditText(), exception.GetType().Name);

            return (RecentInterval, RunResult.Failed);
        }
    }

    private async Task<TimeSpan> TimeUntilFullRunAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var sessions = scope.ServiceProvider.GetRequiredService<IIntegritySessions>();

        await using var session = await sessions.OpenAsync(stoppingToken);

        var last = await session.FindLastRunAsync(IntegrityMode.Full, stoppingToken);

        if (last is null)
        {
            return TimeSpan.Zero;
        }

        var now = await session.GetDatabaseNowAsync(stoppingToken);

        return FullInterval - (now - last.RecordedAt);
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Boundary of a background loop: the failure is logged and the next run tries again.")]
    private async Task<RunResult> RunOnceAsync(IntegrityMode mode, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            var handler = scope.ServiceProvider.GetRequiredService<RunIntegrityCheckHandler>();

            await handler.HandleAsync(CommandFor(mode), stoppingToken);

            return RunResult.Completed;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return RunResult.Cancelled;
        }
        catch (Exception exception)
        {
            WorkerLog.IntegrityRunFailed(_logger, mode.AuditText(), exception.GetType().Name);

            return RunResult.Failed;
        }
    }

    private RunIntegrityCheckCommand CommandFor(IntegrityMode mode)
    {
        return new RunIntegrityCheckCommand(
            mode,
            RecentInterval,
            RecentOverlap,
            FullLookback,
            TimeSpan.FromMinutes(_options.ChainSliceMinutes),
            _options.HeadBatchSize);
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Integrity check service started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Integrity check service is stopping")]
    private static partial void LogStopping(ILogger logger);

    private enum RunResult
    {
        Completed,
        Failed,
        Cancelled
    }
}
