namespace Ledger.Application.Outbox;

public enum PublishFailureReason
{
    BrokerUnavailable = 0,
    Nack = 1,
    Timeout = 2,
    Serialization = 3,
    Unroutable = 4
}
