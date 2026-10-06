using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

[SuppressMessage("Naming", "CA1711",
    Justification = "The outbox queue is a table backed work queue and has nothing to do with System.Collections.Queue.")]
public interface IOutboxQueue
{
    Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken);

    Task<int> MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<int> ReleaseAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken);

    Task<OutboxStats> ReadStatsAsync(OutboxStatsRequest request, CancellationToken cancellationToken);
}
