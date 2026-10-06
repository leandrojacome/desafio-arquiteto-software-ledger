using Ledger.Application.Idempotency;
using Ledger.Application.Outbox;
using Ledger.Application.Security;

namespace Ledger.Worker;

internal sealed class IdempotencyPruneService(
    WorkerLoopHost host,
    IdempotencyPruneSettings settings,
    ILogger<IdempotencyPruneService> logger)
    : WorkerLoopService(WorkerLoop.IdempotencyPrune, host, logger)
{
    protected override async Task<TimeSpan> CycleAsync(IServiceProvider services, CancellationToken stoppingToken)
    {
        await services.GetRequiredService<PruneIdempotencyKeysHandler>().HandleAsync(stoppingToken);

        return settings.Interval;
    }
}
