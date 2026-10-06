using Ledger.Application.Outbox;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed partial class OutboxPublisherService : WorkerLoopService
{
    private static readonly TimeSpan NotClaimedDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShutdownMargin = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumDrainGrace = TimeSpan.FromSeconds(1);

    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly TimeSpan _drainGrace;

    public OutboxPublisherService(
        WorkerLoopHost host,
        IOptions<OutboxOptions> options,
        IOptions<HostOptions> hostOptions,
        ILogger<OutboxPublisherService> logger)
        : base(WorkerLoop.Outbox, host, logger)
    {
        _options = options.Value;
        _timeProvider = host.TimeProvider;
        _logger = logger;
        _drainGrace = DrainGraceFor(hostOptions.Value.ShutdownTimeout);
    }

    protected override async Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        var handler = services.GetRequiredService<PublishOutboxBatchHandler>();

        using var drain = new DrainToken(_drainGrace, _timeProvider, stoppingToken);

        var outcome = await handler.HandleAsync(new PublishOutboxBatchCommand(), drain.Token);

        return DelayAfter(outcome);
    }

    protected override void OnStarted() => LogStarted(_logger);

    protected override void OnStopping() => LogStopping(_logger);

    private static TimeSpan DrainGraceFor(TimeSpan shutdownTimeout)
    {
        var grace = shutdownTimeout - ShutdownMargin;

        return grace < MinimumDrainGrace ? MinimumDrainGrace : grace;
    }

    private TimeSpan DelayAfter(PublishOutboxBatchOutcome outcome)
    {
        if (!outcome.WasClaimed)
        {
            return NotClaimedDelay;
        }

        return outcome.Claimed >= _options.BatchSize
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(_options.IdlePollMs);
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information, Message = "Outbox publisher service started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Outbox publisher service is stopping")]
    private static partial void LogStopping(ILogger logger);
}
