using Ledger.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Outbox;

public sealed class PruneOutboxHandler(
    IOutboxQueue queue,
    IOutboxTelemetry telemetry,
    OutboxSettings settings,
    ILogger<PruneOutboxHandler> logger)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var removed = 0;
        int lastBatch;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastBatch = await queue.PruneAsync(settings.Retention, settings.PruneBatchSize, cancellationToken);
            removed += lastBatch;
        }
        while (lastBatch >= settings.PruneBatchSize);

        if (removed > 0)
        {
            telemetry.Pruned(removed);
        }

        OutboxLog.OutboxPruned(logger, removed > 0 ? LogLevel.Information : LogLevel.Debug, removed);

        return removed;
    }
}
