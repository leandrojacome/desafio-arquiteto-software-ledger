using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using RabbitMQ.Client;

namespace Ledger.Api.IntegrationTests.Outbox;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Resilience")]
[Trait("Speed", "Slow")]
public sealed class BrokerConnectionTests(RabbitMqFixture broker)
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingOutboxTelemetry _telemetry = new();
    private readonly LogCapture<BrokerConnection> _log = new();

    [DockerFact]
    public async Task TryConnect_WithTheBrokerUp_ConnectsDeclaresTheExchangeAndReportsTheConnection()
    {
        await using var connection = CreateConnection();

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();

        connection.IsConnected.ShouldBeTrue();
        _telemetry.Connections.ShouldContain(true);
        _log.Events.ShouldContain(logged => logged.Id == 3006 && logged.Level == LogLevel.Information);
        (await ExchangeExistsAsync()).ShouldBeTrue();
    }

    [DockerFact]
    public async Task TryConnect_WhenAlreadyConnected_DoesNotOpenAnotherConnection()
    {
        await using var connection = CreateConnection();

        await connection.TryConnectAsync(CancellationToken.None);

        var channel = connection.CurrentChannel;

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();

        connection.CurrentChannel.ShouldBeSameAs(channel);
        _log.Events.Count(logged => logged.Id == 3006).ShouldBe(1);
    }

    [DockerFact]
    public async Task TryConnect_WhileTheBrokerIsDown_BacksOffExponentiallyAndRecoversWhenItReturns()
    {
        await using var connection = CreateConnection();

        await broker.StopAsync(CancellationToken.None);

        try
        {
            (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeFalse();
            (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeFalse();

            var firstFailure = _log.Events.Where(logged => logged.Id == 3007).ToList().ShouldHaveSingleItem();

            firstFailure.Level.ShouldBe(LogLevel.Warning);
            firstFailure.Properties["Attempt"].ShouldBe(1);
            ((double)(firstFailure.Properties["NextDelaySeconds"] ?? 0d)).ShouldBeInRange(1d, 1.2d);

            _time.Advance(TimeSpan.FromSeconds(2));

            (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeFalse();

            var failures = _log.Events.Where(logged => logged.Id == 3007).ToList();

            failures.Count.ShouldBe(2);
            failures[1].Properties["Attempt"].ShouldBe(2);
            ((double)(failures[1].Properties["NextDelaySeconds"] ?? 0d)).ShouldBeInRange(1.6d, 2.4d);
            _telemetry.Connections.ShouldNotContain(true);
        }
        finally
        {
            await broker.StartAsync(CancellationToken.None);
        }

        _time.Advance(TimeSpan.FromSeconds(5));

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();

        _telemetry.Connections[^1].ShouldBeTrue();
        connection.IsConnected.ShouldBeTrue();
    }

    [DockerFact]
    public async Task APublishFailure_DiscardsTheChannelAndTheNextConnectRecreatesItWithoutWaiting()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);
        await DeleteExchangeAsync();

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.BrokerUnavailable);
        connection.IsConnected.ShouldBeFalse();
        publisher.ClaimBudget(200).ShouldBe(0);

        (await connection.TryConnectAsync(CancellationToken.None)).ShouldBeTrue();

        await publisher.PublishAsync(Envelope(), CancellationToken.None);

        connection.IsConnected.ShouldBeTrue();
        _telemetry.Connections.ShouldContain(false);
        _telemetry.Connections[^1].ShouldBeTrue();
    }

    [DockerFact]
    public async Task AChannelTornDownInTheMiddleOfABatchOfTwoHundred_FailsEveryMessageCountedAndNothingEscapes()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);
        var circuit = new CircuitBreakingEventPublisher(
            publisher,
            Options.Create(new BrokerCircuitOptions()),
            _telemetry,
            TimeProvider.System,
            new LogCapture<CircuitBreakingEventPublisher>());
        var queue = new InMemoryOutboxQueue(TimeProvider.System);
        var settings = new OutboxSettings(
            200,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5),
            5,
            1000,
            1_000_000,
            TimeSpan.FromDays(7),
            5000);

        for (var index = 0; index < 200; index++)
        {
            queue.Add(Envelope() with { CreatedAt = DateTimeOffset.UnixEpoch.AddSeconds(index), Attempts = 0 });
        }

        await connection.TryConnectAsync(CancellationToken.None);
        await DeleteExchangeAsync();

        var handler = new PublishOutboxBatchHandler(
            queue,
            circuit,
            _telemetry,
            settings,
            TimeProvider.System,
            new LogCapture<PublishOutboxBatchHandler>());

        var outcome = await handler.HandleAsync(new PublishOutboxBatchCommand(), CancellationToken.None);

        outcome.Claimed.ShouldBe(200);
        outcome.Confirmed.ShouldBe(0);
        _telemetry.Failures.Count.ShouldBe(200);
        circuit.Circuit.ShouldBe(BrokerCircuitState.Open);
        queue.Snapshot().ShouldAllBe(row => !row.Published);
    }

    [DockerFact]
    public async Task Probe_WithAConnectedBroker_IsConfirmedAndLeavesNoMessageBehindForAnyConsumer()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeTrue();
        connection.IsConnected.ShouldBeTrue();
    }

    [DockerFact]
    public async Task Probe_WithoutAConnection_IsNotConfirmed()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeFalse();
    }

    [DockerFact]
    public async Task Probe_WhenTheExchangeIsGone_IsNotConfirmedAndDiscardsTheChannel()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        await connection.TryConnectAsync(CancellationToken.None);
        await DeleteExchangeAsync();

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeFalse();
        connection.IsConnected.ShouldBeFalse();
    }

    [DockerFact]
    public async Task ClaimBudget_ReflectsTheConnectionState()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        publisher.ClaimBudget(200).ShouldBe(0);

        await connection.TryConnectAsync(CancellationToken.None);

        publisher.ClaimBudget(200).ShouldBe(200);
    }

    [DockerFact]
    public async Task Publish_WithoutAConnection_FailsAsBrokerUnavailable()
    {
        await using var connection = CreateConnection();
        var publisher = new RabbitMqEventPublisher(connection, Options.Create(BrokerOptions()), TimeProvider.System);

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.BrokerUnavailable);
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

    private RabbitMqOptions BrokerOptions() =>
        new()
        {
            Host = broker.Host,
            Port = broker.Port,
            Username = RabbitMqFixture.Username,
            Password = RabbitMqFixture.Password,
            ConnectTimeoutSeconds = 2,
            ReconnectMinSeconds = 1,
            ReconnectMaxSeconds = 2,
            ReconnectJitterPercent = 20
        };

    private BrokerConnection CreateConnection() =>
        new(Options.Create(BrokerOptions()), _telemetry, _time, _log);

    private async Task<bool> ExchangeExistsAsync()
    {
        await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        try
        {
            await channel.ExchangeDeclarePassiveAsync(RabbitMqFixture.Exchange, CancellationToken.None);

            return true;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return false;
        }
    }

    private async Task DeleteExchangeAsync()
    {
        await using var connection = await broker.CreateConnectionAsync(CancellationToken.None);
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeleteAsync(RabbitMqFixture.Exchange, ifUnused: false, noWait: false, CancellationToken.None);
    }
}
