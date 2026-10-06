using System.Diagnostics.CodeAnalysis;
using System.Text;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Abstractions;

public interface IOutbox
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed record OutboxMessage(
    Guid Id,
    AccountId AccountId,
    string Type,
    string Payload,
    string CorrelationId,
    string? TraceParent)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Id = ").Append(Id).Append(", AccountId = ").Append(AccountId).Append(", Type = ").Append(Type);

        return true;
    }
}

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

public interface IEventPublisher
{
    BrokerCircuitState Circuit { get; }

    bool IsConnected { get; }

    int ClaimBudget(int configuredBatchSize);

    Task<bool> TryConnectAsync(CancellationToken cancellationToken);

    Task<bool> ProbeAsync(CancellationToken cancellationToken);

    Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken);
}
