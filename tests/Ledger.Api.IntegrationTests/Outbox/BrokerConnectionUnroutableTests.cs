using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RabbitMQ.Client.Exceptions;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
public sealed class BrokerConnectionUnroutableTests(RabbitMqFixture broker) : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingOutboxTelemetry _telemetry = new();
    private readonly LogCapture<BrokerConnection> _log = new();
    private readonly string _exchange = $"test.events.{Guid.NewGuid():N}";
    private readonly string _queue = $"test.retention.{Guid.NewGuid():N}";

    private RetentionQueueReader Retention => new(broker, _queue, _exchange);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!broker.IsStopped)
        {
            await Retention.DeleteQuietlyAsync();
        }
    }

    [DockerFact]
    public async Task Publish_WithNoQueueBound_FailsAsUnroutableAndOnlyReconnectsAfterTheBackoff()
    {
        await using var connection = CreateConnection(retention: false);
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions(retention: false)), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.Unroutable);
        var returned = failure.InnerException.ShouldBeOfType<PublishReturnException>();

        returned.ReplyCode.ShouldBe((ushort)312);
        returned.ReplyText.ShouldBe("NO_ROUTE");
        returned.RoutingKey.ShouldBe("EntryRegistered");
        connection.IsConnected.ShouldBeFalse();
        publisher.ClaimBudget(200).ShouldBe(0);
        _telemetry.Connections[^1].ShouldBeFalse();

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeFalse();

        _time.Advance(TimeSpan.FromSeconds(5));

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();
        connection.IsConnected.ShouldBeTrue();
        _telemetry.Connections[^1].ShouldBeTrue();
    }

    [DockerFact]
    public async Task Publish_AfterTheRetentionQueueIsDeleted_IsUnroutableUntilTheReconnectDeclaresItAgain()
    {
        await using var connection = CreateConnection(retention: true);
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions(retention: true)), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);
        await Retention.DeleteQueueAsync();

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.Unroutable);

        _time.Advance(TimeSpan.FromSeconds(5));

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();

        await publisher.PublishAsync(Envelope(), CancellationToken.None);

        (await Retention.CountAsync()).ShouldBe(1u);
        _log.Events.Count(logged => logged.Id == 3011).ShouldBe(1);
        _log.Events.Single(logged => logged.Id == 3011).Level.ShouldBe(LogLevel.Information);
    }

    [DockerFact]
    public async Task FiveHundredConcurrentPublishesWithNoQueue_AreAllReportedUnroutable()
    {
        await using var connection = CreateConnection(retention: false);
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions(retention: false)), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 500).Select(_ => PublishAndClassifyAsync(publisher)));

        outcomes.ShouldAllBe(outcome => outcome == PublishFailureReason.Unroutable);
    }

    [DockerFact]
    public async Task FiveHundredConcurrentPublishesWithTheRetentionQueue_AreAllRoutedAndRetained()
    {
        await using var connection = CreateConnection(retention: true);
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions(retention: true)), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 500).Select(_ => PublishAndClassifyAsync(publisher)));

        outcomes.ShouldAllBe(outcome => outcome == null);
        connection.IsConnected.ShouldBeTrue();
        (await Retention.CountAsync()).ShouldBe(500u);
    }

    [DockerFact]
    public async Task Probe_WithNoQueueBound_IsStillConfirmedBecauseItIsNotMandatory()
    {
        await using var connection = CreateConnection(retention: false);
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions(retention: false)), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeTrue();
        connection.IsConnected.ShouldBeTrue();
    }

    private static async Task<PublishFailureReason?> PublishAndClassifyAsync(RabbitMqEventPublisher publisher)
    {
        try
        {
            await publisher.PublishAsync(Envelope(), CancellationToken.None);

            return null;
        }
        catch (EventPublishException failure)
        {
            return failure.Reason;
        }
    }

    private static OutboxEnvelope Envelope() =>
        new(
            Guid.CreateVersion7(),
            AccountId.From(Guid.NewGuid()).Value,
            "EntryRegistered",
            "{}",
            "correlation",
            null,
            DateTimeOffset.FromUnixTimeSeconds(1_790_000_000L),
            1);

    private RabbitMqOptions BrokerOptions(bool retention) =>
        new()
        {
            Host = broker.Host,
            Port = broker.Port,
            Username = RabbitMqFixture.Username,
            Password = RabbitMqFixture.Password,
            Exchange = _exchange,
            ConnectTimeoutSeconds = 2,
            ReconnectMinSeconds = 1,
            ReconnectMaxSeconds = 2,
            ReconnectJitterPercent = 20,
            Retention = new RetentionQueueOptions { Enabled = retention, Queue = _queue }
        };

    private BrokerConnection CreateConnection(bool retention) =>
        new(Options.Create(BrokerOptions(retention)), _telemetry, _time, _log);
}
