using Ledger.Application.Outbox;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal sealed class OutboxPruneService(
    WorkerLoopHost host,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPruneService> logger)
    : WorkerLoopService(WorkerLoop.OutboxPrune, host, logger)
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(options.Value.PruneIntervalMinutes);

    protected override async Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        await services.GetRequiredService<PruneOutboxHandler>().HandleAsync(stoppingToken);

        return _interval;
    }
}
