using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Messaging;

[Trait("Category", "Unit")]
public sealed class NonBrokerFailureTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingOutboxTelemetry _telemetry = new();

    [Fact]
    public async Task AnEventTheLedgerCannotAssemble_FailsAsSerializationEvenWithoutAnyBrokerConnection()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(Defective(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.Serialization);
        failure.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task TwoHundredDefectiveEvents_NeverOpenTheCircuitNorStopTheHealthyOnesFromBeingAttempted()
    {
        await using var connection = CreateConnection();
        var inner = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);
        var circuit = new CircuitBreakingEventPublisher(
            inner,
            Options.Create(new BrokerCircuitOptions()),
            _telemetry,
            _time,
            new LogCapture<CircuitBreakingEventPublisher>());

        for (var occurrence = 0; occurrence < 200; occurrence++)
        {
            var failure = await Should.ThrowAsync<EventPublishException>(
                () => circuit.PublishAsync(Defective(), CancellationToken.None));

            failure.Reason.ShouldBe(PublishFailureReason.Serialization);
        }

        circuit.Circuit.ShouldBe(BrokerCircuitState.Closed);

        var healthy = await Should.ThrowAsync<EventPublishException>(
            () => circuit.PublishAsync(Healthy(), CancellationToken.None));

        healthy.Reason.ShouldBe(PublishFailureReason.BrokerUnavailable);
        _telemetry.CircuitStates.ShouldBeEmpty();
    }

    private static OutboxEnvelope Defective() => Healthy() with { Payload = "{\"broken\": \"\ud800\"}" };

    private static OutboxEnvelope Healthy() =>
        new(
            Guid.CreateVersion7(),
            AccountId.From(Guid.NewGuid()).Value,
            "EntryRegistered",
            "{}",
            "correlation",
            null,
            DateTimeOffset.FromUnixTimeSeconds(1_790_000_000L),
            1);

    private static RabbitMqOptions BrokerOptions() =>
        new() { Host = "127.0.0.1", Port = 1, Username = "user", Password = "password" };

    private BrokerConnection CreateConnection() =>
        new(Options.Create(BrokerOptions()), _telemetry, _time, new LogCapture<BrokerConnection>());
}
