using System.Text;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Messaging;
using RabbitMQ.Client;

namespace Ledger.Api.IntegrationTests.Messaging;

[Trait("Category", "Unit")]
public sealed class EventEnvelopeMapperTests
{
    private static readonly Guid MessageId = Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75");
    private static readonly Guid Account = Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");

    [Fact]
    public void Properties_MapEveryColumnOfTheEnvelope()
    {
        var envelope = Envelope("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

        var properties = EventEnvelopeMapper.Properties(envelope);

        properties.MessageId.ShouldBe("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75");
        properties.Type.ShouldBe("EntryRegistered");
        properties.CorrelationId.ShouldBe("9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");
        properties.ContentType.ShouldBe("application/json");
        properties.ContentEncoding.ShouldBe("utf-8");
        properties.DeliveryMode.ShouldBe(DeliveryModes.Persistent);
        properties.Timestamp.UnixTime.ShouldBe(1_790_000_000L);
        properties.Headers.ShouldNotBeNull();
        properties.Headers["account-id"].ShouldBe("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33");
        properties.Headers["schema-version"].ShouldBe(1);
        properties.Headers["traceparent"].ShouldBe("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");
    }

    [Fact]
    public void Properties_WithoutTraceParent_OmitTheHeader()
    {
        var properties = EventEnvelopeMapper.Properties(Envelope(null));

        properties.Headers.ShouldNotBeNull();
        properties.Headers.ShouldNotContainKey("traceparent");
    }

    [Fact]
    public void Properties_WithAnEmptyTraceParent_OmitTheHeader()
    {
        var properties = EventEnvelopeMapper.Properties(Envelope(string.Empty));

        properties.Headers.ShouldNotBeNull();
        properties.Headers.ShouldNotContainKey("traceparent");
    }

    [Fact]
    public void Body_IsThePayloadTextEncodedAsUtf8WithoutReserializing()
    {
        const string Payload = "{\"amount\": \"80.00\", \"eventId\": \"x\", \"note\": \"ação\"}";

        var body = EventEnvelopeMapper.Body(Envelope(null) with { Payload = Payload });

        Encoding.UTF8.GetString(body).ShouldBe(Payload);
    }

    [Fact]
    public void Body_WithALoneSurrogate_ThrowsInsteadOfWritingReplacementCharacters()
    {
        var envelope = Envelope(null) with { Payload = "{\"broken\": \"\ud800\"}" };

        Should.Throw<EncoderFallbackException>(() => EventEnvelopeMapper.Body(envelope));
    }

    private static OutboxEnvelope Envelope(string? traceParent) =>
        new(
            MessageId,
            AccountId.From(Account).Value,
            "EntryRegistered",
            "{}",
            "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10",
            traceParent,
            DateTimeOffset.FromUnixTimeSeconds(1_790_000_000L),
            1);
}
