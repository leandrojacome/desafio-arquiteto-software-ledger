using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class InMemoryOutboxQueue(TimeProvider timeProvider) : IOutboxQueue
{
    private readonly Lock _gate = new();
    private readonly List<Row> _rows = [];

    public IReadOnlyList<(Guid Id, int Attempts, bool Published)> Snapshot()
    {
        lock (_gate)
        {
            return [.. _rows.Select(row => (row.Envelope.Id, row.Envelope.Attempts, row.Published))];
        }
    }

    public void Add(OutboxEnvelope envelope)
    {
        lock (_gate)
        {
            _rows.Add(new Row(envelope));
        }
    }

    public Task<IReadOnlyList<OutboxEnvelope>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            var claimed = new List<OutboxEnvelope>();

            foreach (var row in _rows
                         .Where(row => !row.Published && (row.LockedUntil is null || row.LockedUntil < now))
                         .OrderBy(row => row.Envelope.CreatedAt)
                         .Take(batchSize))
            {
                row.LockedUntil = now + lease;
                row.Envelope = row.Envelope with { Attempts = row.Envelope.Attempts + 1 };
                claimed.Add(row.Envelope);
            }

            return Task.FromResult<IReadOnlyList<OutboxEnvelope>>(claimed);
        }
    }

    public Task<int> MarkPublishedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var marked = 0;

            foreach (var row in _rows.Where(row => ids.Contains(row.Envelope.Id)))
            {
                row.Published = true;
                row.LockedUntil = null;
                marked++;
            }

            return Task.FromResult(marked);
        }
    }

    public Task<int> ReleaseAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var released = 0;

            foreach (var row in _rows.Where(row => !row.Published && ids.Contains(row.Envelope.Id)))
            {
                row.LockedUntil = null;
                row.Envelope = row.Envelope with { Attempts = Math.Max(0, row.Envelope.Attempts - 1) };
                released++;
            }

            return Task.FromResult(released);
        }
    }

    public Task<int> PruneAsync(TimeSpan retention, int batchSize, CancellationToken cancellationToken) =>
        Task.FromResult(0);

    public Task<OutboxStats> ReadStatsAsync(OutboxStatsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var pending = _rows.Where(row => !row.Published).OrderBy(row => row.Envelope.CreatedAt).ToList();
            var failed = pending
                .Take(request.FailedHeadWindow)
                .Count(row => row.Envelope.Attempts >= request.FailedAttempts);

            return Task.FromResult(new OutboxStats(pending.Count, null, failed));
        }
    }

    private sealed class Row(OutboxEnvelope envelope)
    {
        public OutboxEnvelope Envelope { get; set; } = envelope;

        public DateTimeOffset? LockedUntil { get; set; }

        public bool Published { get; set; }
    }
}
