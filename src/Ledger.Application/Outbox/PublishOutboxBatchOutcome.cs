namespace Ledger.Application.Outbox;

public sealed record PublishOutboxBatchOutcome
{
    private PublishOutboxBatchOutcome(bool wasClaimed, int claimed, int confirmed, BrokerCircuitState circuit)
    {
        WasClaimed = wasClaimed;
        Claimed = claimed;
        Confirmed = confirmed;
        Circuit = circuit;
    }

    public bool WasClaimed { get; }

    public int Claimed { get; }

    public int Confirmed { get; }

    public BrokerCircuitState Circuit { get; }

    public static PublishOutboxBatchOutcome NotClaimed(BrokerCircuitState circuit) => new(false, 0, 0, circuit);

    public static PublishOutboxBatchOutcome Completed(int claimed, int confirmed, BrokerCircuitState circuit) =>
        new(true, claimed, confirmed, circuit);
}
