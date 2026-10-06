namespace Ledger.Application.Outbox;

public static class PublishFailureReasonExtensions
{
    public static string ToLabel(this PublishFailureReason reason)
    {
        return reason switch
        {
            PublishFailureReason.BrokerUnavailable => "broker_unavailable",
            PublishFailureReason.Nack => "nack",
            PublishFailureReason.Timeout => "timeout",
            PublishFailureReason.Serialization => "serialization",
            PublishFailureReason.Unroutable => "unroutable",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown publish failure reason.")
        };
    }
}
