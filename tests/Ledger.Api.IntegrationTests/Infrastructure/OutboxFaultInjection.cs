using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class FaultInjectingEventPublisher(IEventPublisher inner) : IEventPublisher
{
    private readonly ConcurrentDictionary<Guid, PublishFailureReason> _failing = new();
    private readonly ConcurrentQueue<Guid> _attempted = new();
    private readonly ConcurrentQueue<Guid> _confirmed = new();
    private readonly TaskCompletionSource _firstPublicationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public Func<OutboxEnvelope, bool>? AlwaysFail { get; set; }

    public PublishFailureReason AlwaysFailReason { get; set; } = PublishFailureReason.Nack;

    public Task FirstPublicationStarted => _firstPublicationStarted.Task;

    public IReadOnlyList<Guid> Attempted => [.. _attempted];

    public IReadOnlyList<Guid> Confirmed => [.. _confirmed];

    public BrokerCircuitState Circuit => inner.Circuit;

    public bool IsConnected => inner.IsConnected;

    public void FailEnvelope(Guid id, PublishFailureReason reason) => _failing[id] = reason;

    public int ClaimBudget(int configuredBatchSize) => inner.ClaimBudget(configuredBatchSize);

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken) => inner.TryConnectAsync(cancellationToken);

    public Task<bool> ProbeAsync(CancellationToken cancellationToken) => inner.ProbeAsync(cancellationToken);

    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _attempted.Enqueue(envelope.Id);
        _firstPublicationStarted.TrySetResult();

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (_failing.TryGetValue(envelope.Id, out var reason))
        {
            throw new EventPublishException(reason, envelope.Id);
        }

        if (AlwaysFail?.Invoke(envelope) == true)
        {
            throw new EventPublishException(AlwaysFailReason, envelope.Id);
        }

        await inner.PublishAsync(envelope, cancellationToken);

        _confirmed.Enqueue(envelope.Id);
    }
}

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
