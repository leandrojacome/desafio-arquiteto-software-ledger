using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IOutboxTelemetry
{
    IOutboxPollOperation BeginPoll();

    IOutboxPublishOperation BeginPublish(OutboxEnvelope envelope);

    void Published(int count);

    void PublishFailed(PublishFailureReason reason);

    void Pruned(int count);

    void Measured(OutboxStats stats);

    void CircuitStateChanged(BrokerCircuitState state);

    void BrokerConnected(bool connected);
}
