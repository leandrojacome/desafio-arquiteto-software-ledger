using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Messaging;

internal static partial class MessagingLog
{
    [LoggerMessage(
        EventId = 3003,
        EventName = "OutboxCircuitStateChanged",
        Level = LogLevel.Warning,
        Message = "Broker circuit changed from {From} to {To}")]
    public static partial void OutboxCircuitStateChanged(ILogger logger, string from, string to);

    [LoggerMessage(
        EventId = 3006,
        EventName = "BrokerConnected",
        Level = LogLevel.Information,
        Message = "Broker connection and topology are ready on exchange {Exchange}")]
    public static partial void BrokerConnected(ILogger logger, string exchange);

    [LoggerMessage(
        EventId = 3007,
        EventName = "BrokerConnectionFailed",
        Level = LogLevel.Warning,
        Message = "Broker connection attempt {Attempt} failed ({ExceptionType}), next attempt in {NextDelaySeconds} seconds")]
    public static partial void BrokerConnectionFailed(
        ILogger logger,
        int attempt,
        string exceptionType,
        double nextDelaySeconds);

    [LoggerMessage(
        EventId = 9103,
        EventName = "BrokerProbeDegraded",
        Level = LogLevel.Warning,
        Message = "The broker connectivity probe failed ({ExceptionType})")]
    public static partial void BrokerProbeDegraded(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 3010,
        EventName = "RetentionQueueKeptAsFound",
        Level = LogLevel.Warning,
        Message = "The retention queue {Queue} already exists with other arguments and was kept as it is, changing its limits needs the queue to be deleted and declared again")]
    public static partial void RetentionQueueKeptAsFound(ILogger logger, string queue);

    [LoggerMessage(
        EventId = 3011,
        EventName = "OutboxRoutingRestored",
        Level = LogLevel.Information,
        Message = "The broker is routing outbox messages to a queue again")]
    public static partial void OutboxRoutingRestored(ILogger logger);
}
