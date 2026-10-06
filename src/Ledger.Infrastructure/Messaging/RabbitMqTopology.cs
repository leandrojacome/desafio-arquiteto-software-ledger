using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Infrastructure.Messaging;

internal static class RabbitMqTopology
{
    public const string RoutingKey = "EntryRegistered";
    public const string DeadLetterRoutingKey = "dead";

    private const string QuorumQueueType = "quorum";
    private const string DropHeadOverflow = "drop-head";
    private const long MillisecondsPerHour = 3_600_000L;
    private const long BytesPerMegabyte = 1_048_576L;

    public static async Task<TopologyOutcome> DeclareAsync(
        IConnection connection,
        string exchange,
        RetentionQueueOptions retention,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(retention);

        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            await DeclareExchangeAsync(channel, exchange, ExchangeType.Topic, cancellationToken);
        }

        if (!retention.Enabled)
        {
            return TopologyOutcome.RetentionDisabled;
        }

        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            await DeclareExchangeAsync(channel, retention.DeadLetterExchange, ExchangeType.Direct, cancellationToken);
        }

        var matched = await DeclareQueueAsync(
            connection,
            retention.DeadLetterQueue,
            DeadLetterQueueArguments(retention),
            cancellationToken);

        matched &= await DeclareQueueAsync(
            connection,
            retention.Queue,
            RetentionQueueArguments(retention),
            cancellationToken);

        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            await channel.QueueBindAsync(
                retention.DeadLetterQueue,
                retention.DeadLetterExchange,
                DeadLetterRoutingKey,
                null,
                false,
                cancellationToken);

            await channel.QueueBindAsync(retention.Queue, exchange, RoutingKey, null, false, cancellationToken);
        }

        return matched ? TopologyOutcome.Declared : TopologyOutcome.RetentionKeptAsFound;
    }

    public static Dictionary<string, object?> RetentionQueueArguments(RetentionQueueOptions retention)
    {
        ArgumentNullException.ThrowIfNull(retention);

        return new Dictionary<string, object?>
        {
            ["x-queue-type"] = QuorumQueueType,
            ["x-message-ttl"] = retention.TtlHours * MillisecondsPerHour,
            ["x-max-length"] = retention.MaxLength,
            ["x-max-length-bytes"] = retention.MaxMegabytes * BytesPerMegabyte,
            ["x-overflow"] = DropHeadOverflow,
            ["x-dead-letter-exchange"] = retention.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = DeadLetterRoutingKey
        };
    }

    public static Dictionary<string, object?> DeadLetterQueueArguments(RetentionQueueOptions retention)
    {
        ArgumentNullException.ThrowIfNull(retention);

        return new Dictionary<string, object?>
        {
            ["x-queue-type"] = QuorumQueueType,
            ["x-message-ttl"] = retention.DeadLetterTtlHours * MillisecondsPerHour,
            ["x-max-length"] = retention.DeadLetterMaxLength,
            ["x-overflow"] = DropHeadOverflow
        };
    }

    private static Task DeclareExchangeAsync(
        IChannel channel,
        string exchange,
        string type,
        CancellationToken cancellationToken)
    {
        return channel.ExchangeDeclareAsync(
            exchange,
            type,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
    }

    private static async Task<bool> DeclareQueueAsync(
        IConnection connection,
        string queue,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await channel.QueueDeclareAsync(
                queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments,
                noWait: false,
                cancellationToken);

            return true;
        }
        catch (OperationInterruptedException exception)
            when (exception.ShutdownReason?.ReplyCode == Constants.PreconditionFailed)
        {
            return false;
        }
    }
}
