using Ledger.Application.Abstractions;

namespace Ledger.Application.Idempotency;

public sealed class PruneIdempotencyKeysHandler(IIdempotencyKeyPruner pruner, IdempotencyPruneSettings settings)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var removed = 0;
        int lastBatch;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastBatch = await pruner.PruneAsync(settings.Retention, settings.BatchSize, cancellationToken);
            removed += lastBatch;
        }
        while (lastBatch >= settings.BatchSize);

        return removed;
    }
}
