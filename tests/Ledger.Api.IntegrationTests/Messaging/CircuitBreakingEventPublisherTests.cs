using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Messaging;

[Trait("Category", "Unit")]
public sealed class CircuitBreakingEventPublisherTests
{
    private const int Batch = 200;

    private static readonly TimeSpan Break = TimeSpan.FromSeconds(30);

    private readonly ScriptedEventPublisher _inner = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingOutboxTelemetry _telemetry = new();
    private readonly LogCapture<CircuitBreakingEventPublisher> _log = new();

    [Fact]
    public async Task NineFailuresInTheWindow_KeepTheCircuitClosed()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 9, PublishFailureReason.BrokerUnavailable);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        publisher.ClaimBudget(Batch).ShouldBe(Batch);
    }

    [Fact]
    public async Task TenFailuresInTheWindow_OpenTheCircuit()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public async Task HalfOfTenSamplesFailing_OpensTheCircuit()
    {
        var publisher = CreatePublisher();

        await SucceedTimesAsync(publisher, 5);
        await FailTimesAsync(publisher, 5, PublishFailureReason.Timeout);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public async Task ABatchOfTwoHundredThatFailsAsAWhole_OpensTheCircuitImmediately()
    {
        var publisher = CreatePublisher();

        _inner.Behavior = async (envelope, _) =>
        {
            await Task.Yield();
            throw new EventPublishException(PublishFailureReason.BrokerUnavailable, envelope.Id);
        };

        var attempts = Enumerable.Range(0, Batch)
            .Select(_ => AttemptAsync(publisher))
            .ToList();

        var outcomes = await Task.WhenAll(attempts);

        outcomes.ShouldAllBe(outcome => outcome is EventPublishException);
        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public async Task OpenCircuit_RejectsWithoutCallingTheInnerPublisher()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        var callsBefore = _inner.Calls;
        var rejected = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));

        rejected.Reason.ShouldBe(PublishFailureReason.BrokerUnavailable);
        _inner.Calls.ShouldBe(callsBefore);
    }

    [Fact]
    public void ClaimBudget_WhenClosedAndConnected_IsTheConfiguredBatch()
    {
        CreatePublisher().ClaimBudget(Batch).ShouldBe(Batch);
    }

    [Fact]
    public void ClaimBudget_WhenClosedWithoutConnection_IsZero()
    {
        _inner.Connected = false;

        CreatePublisher().ClaimBudget(Batch).ShouldBe(0);
    }

    [Fact]
    public async Task ClaimBudget_WhenNotClosed_IsZeroEvenAfterTheBreakHasElapsed()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        publisher.ClaimBudget(Batch).ShouldBe(0);

        _time.Advance(Break);

        publisher.ClaimBudget(Batch).ShouldBe(0);
    }

    [Fact]
    public async Task AProbeBeforeTheBreakHasElapsed_IsRejectedWithoutTouchingTheBroker()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeFalse();

        _inner.Probes.ShouldBe(0);
        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public async Task ASuccessfulProbeAfterTheBreak_ClosesTheCircuitAndReleasesTheFullBudget()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        _time.Advance(Break);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeTrue();

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        publisher.ClaimBudget(Batch).ShouldBe(Batch);
        _inner.Probes.ShouldBe(1);
        _telemetry.CircuitStates.ShouldBe(
            [BrokerCircuitState.Open, BrokerCircuitState.HalfOpen, BrokerCircuitState.Closed]);
    }

    [Fact]
    public async Task AFailedProbeAfterTheBreak_ReopensTheCircuitForAnotherBreak()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        _time.Advance(Break);
        _inner.ProbeBehavior = _ => Task.FromResult(false);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeFalse();

        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
        publisher.ClaimBudget(Batch).ShouldBe(0);

        _time.Advance(Break - TimeSpan.FromSeconds(1));

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeFalse();
        _inner.Probes.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        _inner.ProbeBehavior = _ => Task.FromResult(true);

        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeTrue();
        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
    }

    [Fact]
    public async Task ARealMessageDoesNotActAsTheProbe_SoARejectedOneCannotReopenTheCircuit()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        _time.Advance(Break);
        (await publisher.ProbeAsync(CancellationToken.None)).ShouldBeTrue();

        _inner.Behavior = (envelope, _) => ScriptedEventPublisher.Fail(PublishFailureReason.Nack, envelope);

        await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        publisher.ClaimBudget(Batch).ShouldBe(Batch);
    }

    [Fact]
    public async Task SerializationFailures_NeverCountForTheCircuit()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 200, PublishFailureReason.Serialization);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
    }

    [Fact]
    public async Task UnroutableFailures_NeverCountForTheCircuitBecauseTheBrokerAnswered()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 200, PublishFailureReason.Unroutable);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
        publisher.ClaimBudget(Batch).ShouldBe(Batch);
        _telemetry.CircuitStates.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnroutableFailures_AreRethrownWithTheirOwnReason()
    {
        var publisher = CreatePublisher();

        _inner.Behavior = (envelope, _) => ScriptedEventPublisher.Fail(PublishFailureReason.Unroutable, envelope);

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.Unroutable);
    }

    [Fact]
    public async Task SerializationFailures_AreRethrownWithTheirOwnReason()
    {
        var publisher = CreatePublisher();

        _inner.Behavior = (envelope, _) => ScriptedEventPublisher.Fail(PublishFailureReason.Serialization, envelope);

        var failure = await Should.ThrowAsync<EventPublishException>(
            () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));

        failure.Reason.ShouldBe(PublishFailureReason.Serialization);
    }

    [Fact]
    public async Task ACancelledPublication_CountsAsABrokerFailure()
    {
        var publisher = CreatePublisher();

        _inner.Behavior = (_, _) => Task.FromException(new OperationCanceledException());

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Should.ThrowAsync<OperationCanceledException>(
                () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));
        }

        publisher.Circuit.ShouldBe(BrokerCircuitState.Open);
    }

    [Fact]
    public async Task FailuresOlderThanTheSamplingWindow_AreForgotten()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 9, PublishFailureReason.Nack);

        _time.Advance(TimeSpan.FromSeconds(31));

        await FailTimesAsync(publisher, 9, PublishFailureReason.Nack);

        publisher.Circuit.ShouldBe(BrokerCircuitState.Closed);
    }

    [Fact]
    public async Task EveryStateChange_IsLoggedAsEvent3003AndReportedToTelemetry()
    {
        var publisher = CreatePublisher();

        await FailTimesAsync(publisher, 10, PublishFailureReason.Nack);

        var opened = _log.Events.ShouldHaveSingleItem();

        opened.Id.ShouldBe(3003);
        opened.Name.ShouldBe("OutboxCircuitStateChanged");
        opened.Level.ShouldBe(LogLevel.Warning);
        opened.Properties["From"].ShouldBe("Closed");
        opened.Properties["To"].ShouldBe("Open");
        _telemetry.CircuitStates.ShouldBe([BrokerCircuitState.Open]);
    }

    [Fact]
    public void ConnectionState_ComesFromTheInnerPublisher()
    {
        var publisher = CreatePublisher();

        publisher.IsConnected.ShouldBeTrue();

        _inner.Connected = false;

        publisher.IsConnected.ShouldBeFalse();
    }

    private static async Task<Exception?> AttemptAsync(CircuitBreakingEventPublisher publisher)
    {
        try
        {
            await publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None);

            return null;
        }
        catch (EventPublishException exception)
        {
            return exception;
        }
    }

    private async Task FailTimesAsync(CircuitBreakingEventPublisher publisher, int times, PublishFailureReason reason)
    {
        _inner.Behavior = (envelope, _) => ScriptedEventPublisher.Fail(reason, envelope);

        for (var attempt = 0; attempt < times; attempt++)
        {
            await Should.ThrowAsync<EventPublishException>(
                () => publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None));
        }
    }

    private async Task SucceedTimesAsync(CircuitBreakingEventPublisher publisher, int times)
    {
        _inner.Behavior = (_, _) => Task.CompletedTask;

        for (var attempt = 0; attempt < times; attempt++)
        {
            await publisher.PublishAsync(ScriptedEventPublisher.Envelope(), CancellationToken.None);
        }
    }

    private CircuitBreakingEventPublisher CreatePublisher()
    {
        return new CircuitBreakingEventPublisher(
            _inner,
            Options.Create(new BrokerCircuitOptions()),
            _telemetry,
            _time,
            _log);
    }
}
