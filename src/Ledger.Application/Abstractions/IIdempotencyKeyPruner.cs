namespace Ledger.Application.Abstractions;

public interface IIdempotencyKeyPruner
{
    Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken);
}
