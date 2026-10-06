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
