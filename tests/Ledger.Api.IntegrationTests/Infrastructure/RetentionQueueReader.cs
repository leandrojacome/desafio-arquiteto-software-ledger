using System.Net.Sockets;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class RetentionQueueReader(RabbitMqFixture broker, string queue, string exchange)
{
    private const ushort NotFound = 404;

    public string Queue { get; } = queue;

    public string DeadLetterQueue => Queue + ".dead-letter";

    public string DeadLetterExchange => Queue + ".dlx";

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        await ExistsAsync(Queue, cancellationToken);

    public async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        try
        {
            await channel.QueueDeclarePassiveAsync(name, cancellationToken);

            return true;
        }
        catch (OperationInterruptedException exception) when (exception.ShutdownReason?.ReplyCode == NotFound)
        {
            return false;
        }
    }

    public async Task<uint> CountAsync(CancellationToken cancellationToken = default) =>
        await CountAsync(Queue, cancellationToken);

    public async Task<uint> CountAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        return (await channel.QueueDeclarePassiveAsync(name, cancellationToken)).MessageCount;
    }

    public async Task<IReadOnlyList<ReceivedMessage>> DrainAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var received = new List<ReceivedMessage>();

        while (await channel.BasicGetAsync(Queue, autoAck: true, cancellationToken) is { } message)
        {
            received.Add(ReceivedMessage.From(message));
        }

        return received;
    }

    public async Task DeclareWithAsync(
        string name,
        IDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            name,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments,
            noWait: false,
            cancellationToken);
    }

    public async Task DeclareAndBindWithAsync(
        IDictionary<string, object?>? arguments,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
        await channel.QueueDeclareAsync(
            Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments,
            noWait: false,
            cancellationToken);
        await channel.QueueBindAsync(
            Queue,
            exchange,
            RabbitMqFixture.RoutingKey,
            null,
            false,
            cancellationToken);
    }

    public async Task DeleteQueueAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.QueueDeleteAsync(Queue, ifUnused: false, ifEmpty: false, noWait: false, cancellationToken);
    }

    public async Task DeleteQuietlyAsync()
    {
        try
        {
            await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
            await using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeleteAsync(Queue, ifUnused: false, ifEmpty: false, noWait: false, CancellationToken.None);
            await channel.QueueDeleteAsync(DeadLetterQueue, ifUnused: false, ifEmpty: false, noWait: false, CancellationToken.None);
            await channel.ExchangeDeleteAsync(DeadLetterExchange, ifUnused: false, noWait: false, CancellationToken.None);

            if (exchange != RabbitMqFixture.Exchange)
            {
                await channel.ExchangeDeleteAsync(exchange, ifUnused: false, noWait: false, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is IOException or BrokerUnreachableException
                                              or AlreadyClosedException or OperationInterruptedException
                                              or SocketException or TimeoutException)
        {
            return;
        }
    }
}
