namespace Ledger.Application.Outbox;

public enum BrokerCircuitState
{
    Closed = 0,
    HalfOpen = 1,
    Open = 2
}
