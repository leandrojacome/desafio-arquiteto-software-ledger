using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class FaultInjectingOutboxQueue(IOutboxQueue inner) : IOutboxQueue
{
    private readonly ConcurrentQueue<IReadOnlyList<Guid>> _claims = new();
    private int _markCalls;

    public bool FailMarking { get; set; }

    public IReadOnlyList<IReadOnlyList<Guid>> Claims => [.. _claims];

    public int MarkCalls => Volatile.Read(ref _markCalls);

    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        var batch = await inner.ClaimBatchAsync(batchSize, lease, cancellationToken);

        if (batch.Count > 0)
        {
            _claims.Enqueue([.. batch.Select(envelope => envelope.Id)]);
        }

        return batch;
    }

    public Task<int> MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _markCalls);

        return FailMarking
            ? Task.FromException<int>(new InvalidOperationException("Simulated crash between publishing and marking."))
            : inner.MarkPublishedAsync(ids, cancellationToken);
    }

    public Task<int> ReleaseAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        inner.ReleaseAsync(ids, cancellationToken);

    public Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken) =>
        inner.PruneAsync(retention, batchSize, cancellationToken);

    public Task<OutboxStats> ReadStatsAsync(OutboxStatsRequest request, CancellationToken cancellationToken) =>
        inner.ReadStatsAsync(request, cancellationToken);
}
