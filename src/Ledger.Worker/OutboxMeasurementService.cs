using Ledger.Application.Outbox;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class OutboxMeasurementService(
    WorkerLoopHost host,
    IOptions<OutboxOptions> options,
    ILogger<OutboxMeasurementService> logger)
    : WorkerLoopService(WorkerLoop.Measure, host, logger)
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(options.Value.MeasureIntervalSeconds);

    protected override async Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        await services.GetRequiredService<MeasureOutboxHandler>().HandleAsync(stoppingToken);

        return _interval;
    }
}
