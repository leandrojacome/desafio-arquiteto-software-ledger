using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Outbox;

public enum PublishFailureReason
{
    BrokerUnavailable = 0,
    Nack = 1,
    Timeout = 2,
    Serialization = 3,
    Unroutable = 4
}

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

[SuppressMessage("Design", "CA1032",
    Justification = "The failure reason and the message id are required, so the standard parameterless constructors would build an invalid exception.")]
public sealed class EventPublishException : Exception
{
    public EventPublishException(PublishFailureReason reason, Guid messageId)
        : base($"The outbox message could not be published ({reason.ToLabel()}).")
    {
        Reason = reason;
        MessageId = messageId;
    }

    public EventPublishException(PublishFailureReason reason, Guid messageId, Exception innerException)
        : base($"The outbox message could not be published ({reason.ToLabel()}).", innerException)
    {
        Reason = reason;
        MessageId = messageId;
    }

    public PublishFailureReason Reason { get; }

    public Guid MessageId { get; }
}
