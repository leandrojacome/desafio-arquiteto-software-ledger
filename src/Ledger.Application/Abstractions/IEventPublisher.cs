using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IEventPublisher
{
    BrokerCircuitState Circuit { get; }

    bool IsConnected { get; }

    int ClaimBudget(int configuredBatchSize);

    Task<bool> TryConnectAsync(CancellationToken cancellationToken);

    Task<bool> ProbeAsync(CancellationToken cancellationToken);

    Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);
}
