using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class RecordingOutboxTelemetry : IOutboxTelemetry
{
    private readonly ConcurrentQueue<BrokerCircuitState> _circuitStates = new();
    private readonly ConcurrentQueue<bool> _connections = new();
    private readonly ConcurrentQueue<PublishFailureReason> _failures = new();

    public IReadOnlyList<BrokerCircuitState> CircuitStates => [.. _circuitStates];

    public IReadOnlyList<bool> Connections => [.. _connections];

    public IReadOnlyList<PublishFailureReason> Failures => [.. _failures];

    public IOutboxPollOperation BeginPoll() => new NoOperation();

    public IOutboxPublishOperation BeginPublish(OutboxEnvelope envelope) => new NoOperation();

    public void Published(int count)
    {
    }

    public void PublishFailed(PublishFailureReason reason) => _failures.Enqueue(reason);

    public void Pruned(int count)
    {
    }

    public void Measured(OutboxStats stats)
    {
    }

    public void CircuitStateChanged(BrokerCircuitState state) => _circuitStates.Enqueue(state);

    public void BrokerConnected(bool connected) => _connections.Enqueue(connected);

    private sealed class NoOperation : IOutboxPollOperation, IOutboxPublishOperation
    {
        public void BatchSize(int size)
        {
        }

        public void Confirmed()
        {
        }

        public void Failed(PublishFailureReason reason)
        {
        }

        public void Dispose()
        {
        }
    }
}
