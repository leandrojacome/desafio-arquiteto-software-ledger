using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Messaging;

internal sealed class ScriptedEventPublisher : IEventPublisher
{
    private int _calls;
    private int _probes;

    public bool Connected { get; set; } = true;

    public Func<OutboxEnvelope, CancellationToken, Task> Behavior { get; set; } = (_, _) => Task.CompletedTask;

    public int Calls => Volatile.Read(ref _calls);

    public BrokerCircuitState Circuit => BrokerCircuitState.Closed;

    public bool IsConnected => Connected;

    public int ClaimBudget(int configuredBatchSize) => Connected ? configuredBatchSize : 0;

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken) => Task.FromResult(Connected);

    public Func<CancellationToken, Task<bool>> ProbeBehavior { get; set; } = _ => Task.FromResult(true);

    public int Probes => Volatile.Read(ref _probes);

    public Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _probes);

        return ProbeBehavior(cancellationToken);
    }

    public Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        return Behavior(envelope, cancellationToken);
    }

    public static OutboxEnvelope Envelope() =>
        new(
            Guid.CreateVersion7(),
            AccountId.From(Guid.NewGuid()).Value,
            "EntryRegistered",
            "{}",
            "correlation",
            null,
            DateTimeOffset.UnixEpoch,
            1);

    public static Task Fail(PublishFailureReason reason, OutboxEnvelope envelope) =>
        Task.FromException(new EventPublishException(reason, envelope.Id));
}
