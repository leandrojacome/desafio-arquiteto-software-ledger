using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Application.Tests.Outbox;

internal sealed class RecordingOutboxTelemetry : IOutboxTelemetry
{
    private readonly ConcurrentQueue<PublishRecord> _publishes = new();
    private readonly ConcurrentQueue<int> _published = new();
    private readonly ConcurrentQueue<PublishFailureReason> _failures = new();
    private readonly ConcurrentQueue<PollRecord> _polls = new();
    private readonly ConcurrentQueue<int> _pruned = new();
    private readonly ConcurrentQueue<OutboxStats> _measured = new();

    public IReadOnlyCollection<PublishRecord> Publishes => _publishes;

    public IReadOnlyCollection<int> Published => _published;

    public IReadOnlyCollection<PublishFailureReason> Failures => _failures;

    public IReadOnlyCollection<PollRecord> Polls => _polls;

    public IReadOnlyCollection<int> Pruned => _pruned;

    public IReadOnlyCollection<OutboxStats> Measured => _measured;

    public IOutboxPollOperation BeginPoll()
    {
        var record = new PollRecord();
        _polls.Enqueue(record);

        return record;
    }

    public IOutboxPublishOperation BeginPublish(OutboxEnvelope envelope)
    {
        var record = new PublishRecord(envelope.Id);
        _publishes.Enqueue(record);

        return record;
    }

    void IOutboxTelemetry.Published(int count) => _published.Enqueue(count);

    public void PublishFailed(PublishFailureReason reason) => _failures.Enqueue(reason);

    void IOutboxTelemetry.Pruned(int count) => _pruned.Enqueue(count);

    void IOutboxTelemetry.Measured(OutboxStats stats) => _measured.Enqueue(stats);

    public void CircuitStateChanged(BrokerCircuitState state)
    {
    }

    public void BrokerConnected(bool connected)
    {
    }

    internal sealed class PollRecord : IOutboxPollOperation
    {
        public int? Size { get; private set; }

        public bool Disposed { get; private set; }

        public void BatchSize(int size)
        {
            Size = size;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    internal sealed class PublishRecord(Guid messageId) : IOutboxPublishOperation
    {
        public Guid MessageId { get; } = messageId;

        public bool WasConfirmed { get; private set; }

        public PublishFailureReason? Failure { get; private set; }

        public bool Disposed { get; private set; }

        public void Confirmed()
        {
            WasConfirmed = true;
        }

        public void Failed(PublishFailureReason reason)
        {
            Failure = reason;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
