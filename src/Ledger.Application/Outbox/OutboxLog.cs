using Microsoft.Extensions.Logging;

namespace Ledger.Application.Outbox;

internal static partial class OutboxLog
{
    [LoggerMessage(
        EventId = 3001,
        EventName = "OutboxBatchPublished",
        Message = "Outbox batch published: {Claimed} claimed, {Published} published")]
    public static partial void OutboxBatchPublished(ILogger logger, LogLevel level, int claimed, int published);

    [LoggerMessage(
        EventId = 3002,
        EventName = "OutboxPublishFailed",
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} could not be published ({Reason}, attempt {Attempts})")]
    public static partial void OutboxPublishFailed(ILogger logger, Guid messageId, string reason, int attempts);

    [LoggerMessage(
        EventId = 3004,
        EventName = "OutboxPruned",
        Message = "Outbox pruned: {Removed} published messages removed")]
    public static partial void OutboxPruned(ILogger logger, LogLevel level, int removed);

    [LoggerMessage(
        EventId = 3005,
        EventName = "OutboxMessageStuck",
        Level = LogLevel.Warning,
        Message = "Outbox message {MessageId} is stuck after {Attempts} attempts")]
    public static partial void OutboxMessageStuck(ILogger logger, Guid messageId, int attempts);

    [LoggerMessage(
        EventId = 3009,
        EventName = "OutboxBatchUnroutable",
        Level = LogLevel.Warning,
        Message = "The broker returned {Unroutable} outbox messages because no queue is bound to the exchange, they stay in the outbox and are tried again with backoff")]
    public static partial void OutboxBatchUnroutable(ILogger logger, int unroutable);
}
