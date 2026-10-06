using System.Text;
using Ledger.Application.Outbox;
using RabbitMQ.Client;

namespace Ledger.Infrastructure.Messaging;

internal static class EventEnvelopeMapper
{
    public const string ContentType = "application/json";
    public const string ContentEncoding = "utf-8";
    public const string AccountIdHeader = "account-id";
    public const string SchemaVersionHeader = "schema-version";
    public const string TraceParentHeader = "traceparent";
    public const int SchemaVersion = 1;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] Body(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return StrictUtf8.GetBytes(envelope.Payload);
    }

    public static BasicProperties Properties(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var headers = new Dictionary<string, object?>
        {
            [AccountIdHeader] = envelope.AccountId.ToString(),
            [SchemaVersionHeader] = SchemaVersion
        };

        if (envelope.TraceParent is { Length: > 0 } traceParent)
        {
            headers[TraceParentHeader] = traceParent;
        }

        return new BasicProperties
        {
            MessageId = envelope.Id.ToString("D"),
            Type = envelope.Type,
            CorrelationId = envelope.CorrelationId,
            ContentType = ContentType,
            ContentEncoding = ContentEncoding,
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(envelope.CreatedAt.ToUnixTimeSeconds()),
            Headers = headers
        };
    }
}
