using System.Globalization;
using System.Text;
using RabbitMQ.Client;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class RawEventPublisher
{
    public static Task PublishAsync(
        RabbitMqFixture broker,
        Guid messageId,
        string body,
        CancellationToken cancellationToken = default) =>
        PublishWithKeyAsync(broker, RabbitMqFixture.RoutingKey, messageId, body, cancellationToken);

    public static async Task PublishWithKeyAsync(
        RabbitMqFixture broker,
        string routingKey,
        Guid messageId,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(broker);

        await using var connection = await broker.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(
            RabbitMqFixture.Exchange,
            ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken);

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString("D"),
            Type = routingKey,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent
        };

        await channel.BasicPublishAsync(
            RabbitMqFixture.Exchange,
            routingKey,
            mandatory: false,
            properties,
            Encoding.UTF8.GetBytes(body),
            cancellationToken);
    }

    public static string EventJson(Guid accountId, long version, decimal balanceAfter, int schemaVersion = 1) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              {"eventId": "{{Guid.NewGuid():D}}", "eventType": "EntryRegistered", "schemaVersion": {{schemaVersion}}, "accountId": "{{accountId:D}}", "accountVersion": {{version}}, "balanceAfter": "{{balanceAfter:F2}}"}
              """);
}
