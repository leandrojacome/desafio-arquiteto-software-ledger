using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class QueueProbe : IAsyncDisposable
{
    private readonly RabbitMqFixture _broker;
    private IConnection _connection;
    private IChannel _channel;

    private QueueProbe(RabbitMqFixture broker, IConnection connection, IChannel channel, string queue)
    {
        _broker = broker;
        _connection = connection;
        _channel = channel;
        Queue = queue;
    }

    public string Queue { get; }

    public static async Task<QueueProbe> CreateAsync(
        RabbitMqFixture broker,
        string routingKey = RabbitMqFixture.RoutingKey,
        string exchange = RabbitMqFixture.Exchange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);

        var connection = await broker.CreateConnectionAsync(cancellationToken);
        var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = $"probe.{Guid.NewGuid():N}";

        await channel.ExchangeDeclareAsync(
            exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);

        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);

        await channel.QueueBindAsync(queue, exchange, routingKey, null, false, cancellationToken);

        return new QueueProbe(broker, connection, channel, queue);
    }

    public async Task<IReadOnlyList<ReceivedMessage>> ReceiveAsync(
        int expected,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var received = new List<ReceivedMessage>(expected);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);

        while (received.Count < expected && !deadline.IsCancellationRequested)
        {
            var message = await _channel.BasicGetAsync(Queue, autoAck: true, deadline.Token);

            if (message is null)
            {
                await DelayQuietlyAsync(deadline.Token);

                continue;
            }

            received.Add(ReceivedMessage.From(message));
        }

        return received;
    }

    public async Task<IReadOnlyList<ReceivedMessage>> DrainAsync(CancellationToken cancellationToken = default)
    {
        var received = new List<ReceivedMessage>();

        while (await _channel.BasicGetAsync(Queue, autoAck: true, cancellationToken) is { } message)
        {
            received.Add(ReceivedMessage.From(message));
        }

        return received;
    }

    public async Task<uint> MessageCountAsync(CancellationToken cancellationToken = default)
    {
        var declared = await _channel.QueueDeclarePassiveAsync(Queue, cancellationToken);

        return declared.MessageCount;
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisposeQuietlyAsync(deleteQueue: false);

        _connection = await _broker.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.QueueDeclareAsync(
            Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeQuietlyAsync(deleteQueue: true);
    }

    private async Task DisposeQuietlyAsync(bool deleteQueue)
    {
        try
        {
            if (deleteQueue && _connection.IsOpen)
            {
                await _channel.QueueDeleteAsync(Queue, ifUnused: false, ifEmpty: false, noWait: false, CancellationToken.None);
            }

            await _channel.DisposeAsync();
            await _connection.DisposeAsync();
        }
        catch (Exception exception) when (exception is IOException or OperationInterruptedException
                                              or AlreadyClosedException or ObjectDisposedException)
        {
            return;
        }
    }

    private static async Task DelayQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }
}

internal sealed record ReceivedMessage(
    string? MessageId,
    string? Type,
    string? CorrelationId,
    long? Timestamp,
    DeliveryModes DeliveryMode,
    string? ContentType,
    string? ContentEncoding,
    IReadOnlyDictionary<string, object?> Headers,
    string RoutingKey,
    string Body)
{
    public static ReceivedMessage From(BasicGetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var properties = result.BasicProperties;
        var headers = (properties.Headers ?? new Dictionary<string, object?>())
            .ToDictionary(pair => pair.Key, pair => Normalize(pair.Value));

        return new ReceivedMessage(
            properties.MessageId,
            properties.Type,
            properties.CorrelationId,
            properties.Timestamp.UnixTime,
            properties.DeliveryMode,
            properties.ContentType,
            properties.ContentEncoding,
            headers,
            result.RoutingKey,
            Encoding.UTF8.GetString(result.Body.Span));
    }

    private static object? Normalize(object? value) => value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value;
}
