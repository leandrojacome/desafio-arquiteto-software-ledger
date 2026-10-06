using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Outbox;

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
