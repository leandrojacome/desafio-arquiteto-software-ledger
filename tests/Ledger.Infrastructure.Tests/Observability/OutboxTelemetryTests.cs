using System.Diagnostics;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Tests.Observability.Support;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class OutboxTelemetryTests : IDisposable
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string CorrelationId = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";

    private static readonly AccountId Account =
        AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Fact]
    public void Published_AddsTheCountWithoutLabels()
    {
        _telemetry.OutboxPort.Published(25);

        var measurement = _telemetry.Capture.Of("outbox.published").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(25);
        measurement.Unit.ShouldBe("{message}");
        measurement.Tags.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PublishFailureReason.BrokerUnavailable, "broker_unavailable")]
    [InlineData(PublishFailureReason.Nack, "nack")]
    [InlineData(PublishFailureReason.Timeout, "timeout")]
    [InlineData(PublishFailureReason.Serialization, "serialization")]
    [InlineData(PublishFailureReason.Unroutable, "unroutable")]
    public void PublishFailed_CountsOneFailureWithTheReason(PublishFailureReason reason, string label)
    {
        _telemetry.OutboxPort.PublishFailed(reason);

        var measurement = _telemetry.Capture.Of("outbox.publish.failures").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Unit.ShouldBe("{failure}");
        measurement.Tags.Keys.ShouldBe(["reason"]);
        measurement.Tag("reason").ShouldBe(label);
    }

    [Fact]
    public void Pruned_AddsTheRemovedMessages()
    {
        _telemetry.OutboxPort.Pruned(5000);

        var measurement = _telemetry.Capture.Of("outbox.pruned").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(5000);
        measurement.Unit.ShouldBe("{message}");
        measurement.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void PublishedAndPruned_WithZero_CountNothing()
    {
        _telemetry.OutboxPort.Published(0);
        _telemetry.OutboxPort.Pruned(0);

        _telemetry.Capture.All.ShouldBeEmpty();
    }

    [Fact]
    public void BeginPublish_Confirmed_ObservesTheDurationOncePerMessage()
    {
        using (var operation = _telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            _telemetry.Time.Advance(TimeSpan.FromMilliseconds(80));
            operation.Confirmed();
        }

        var duration = _telemetry.Capture.Of("outbox.publish.duration").ShouldHaveSingleItem();
        duration.Unit.ShouldBe("s");
        duration.Value.ShouldBe(0.08, 0.0001);
        duration.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void BeginPublish_Failed_DoesNotObserveADuration()
    {
        using (var operation = _telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            operation.Failed(PublishFailureReason.Nack);
        }

        _telemetry.Capture.Of("outbox.publish.duration").ShouldBeEmpty();
    }

    [Fact]
    public void Measured_FeedsTheObservableGaugesWithTheLatestSnapshot()
    {
        _telemetry.OutboxPort.Measured(new OutboxStats(1234, 42.5, 3));

        var pending = _telemetry.Capture.Observe("outbox.pending.messages").ShouldHaveSingleItem();
        pending.Value.ShouldBe(1234);
        pending.Unit.ShouldBe("{message}");
        pending.Tags.ShouldBeEmpty();
        _telemetry.Capture.Observe("outbox.oldest_pending.age").ShouldHaveSingleItem().Value.ShouldBe(42.5);
        _telemetry.Capture.Observe("outbox.failed.messages").ShouldHaveSingleItem().Value.ShouldBe(3);

        _telemetry.OutboxPort.Measured(new OutboxStats(0, 0, 0));

        _telemetry.Capture.Observe("outbox.pending.messages").ShouldHaveSingleItem().Value.ShouldBe(0);
    }

    [Fact]
    public void OldestPendingAge_UsesSecondsAsUnit()
    {
        _telemetry.OutboxPort.Measured(new OutboxStats(1, 1d, 0));

        _telemetry.Capture.Observe("outbox.oldest_pending.age").ShouldHaveSingleItem().Unit.ShouldBe("s");
    }

    [Fact]
    public void ObservableGauges_BeforeAnyMeasurement_EmitNothingBecauseOnlyTheWorkerMeasures()
    {
        _telemetry.Capture.Observe("outbox.pending.messages").ShouldBeEmpty();
        _telemetry.Capture.Observe("outbox.oldest_pending.age").ShouldBeEmpty();
        _telemetry.Capture.Observe("outbox.failed.messages").ShouldBeEmpty();
        _telemetry.Capture.Observe("broker.circuit_breaker.state").ShouldBeEmpty();
        _telemetry.Capture.Observe("broker.connected").ShouldBeEmpty();
        _telemetry.Capture.Observe("worker.last_cycle.timestamp").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BrokerCircuitState.Closed, 0)]
    [InlineData(BrokerCircuitState.HalfOpen, 1)]
    [InlineData(BrokerCircuitState.Open, 2)]
    public void CircuitStateChanged_IsReadAsZeroOneOrTwo(BrokerCircuitState state, int expected)
    {
        _telemetry.OutboxPort.CircuitStateChanged(state);

        var measurement = _telemetry.Capture.Observe("broker.circuit_breaker.state").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(expected);
        measurement.Unit.ShouldBeNull();
        measurement.Tags.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void BrokerConnected_IsReadAsOneOrZero(bool connected, int expected)
    {
        _telemetry.OutboxPort.BrokerConnected(connected);

        _telemetry.Capture.Observe("broker.connected").ShouldHaveSingleItem().Value.ShouldBe(expected);
    }

    [Fact]
    public void WorkerLastCycle_ReadsTheHeartbeatPerLoopInUnixSecondsOnceTheWorkerIsActive()
    {
        var outbox = new DateTimeOffset(2026, 10, 1, 12, 0, 5, TimeSpan.Zero);
        var integrity = new DateTimeOffset(2026, 10, 1, 11, 59, 0, TimeSpan.Zero);
        _telemetry.Heartbeat.Set(WorkerLoop.Outbox, outbox);
        _telemetry.Heartbeat.Set(WorkerLoop.IntegrityRecent, integrity);
        _telemetry.OutboxPort.BrokerConnected(true);

        var measurements = _telemetry.Capture.Observe("worker.last_cycle.timestamp");

        measurements.Count.ShouldBe(2);
        measurements.Single(measurement => measurement.Tag("loop") == "outbox").Value
            .ShouldBe(outbox.ToUnixTimeSeconds());
        measurements.Single(measurement => measurement.Tag("loop") == "integrity-recent").Value
            .ShouldBe(integrity.ToUnixTimeSeconds());
        measurements.ShouldAllBe(measurement => measurement.Unit == "s");
    }

    [Fact]
    public void WorkerLastCycle_SkipsALoopThatNeverBeat()
    {
        _telemetry.Heartbeat.Set(WorkerLoop.Outbox, DateTimeOffset.UnixEpoch.AddSeconds(100));
        _telemetry.OutboxPort.BrokerConnected(true);

        var measurements = _telemetry.Capture.Observe("worker.last_cycle.timestamp");

        measurements.ShouldHaveSingleItem().Tag("loop").ShouldBe("outbox");
    }

    [Fact]
    public void BeginPoll_RecordsTheBatchSizeOnAnInternalSpan()
    {
        using (var operation = _telemetry.OutboxPort.BeginPoll())
        {
            operation.BatchSize(200);
        }

        var span = _telemetry.Capture.SingleActivity("outbox.poll");
        span.Kind.ShouldBe(ActivityKind.Internal);
        span.GetTagItem("outbox.batch_size").ShouldBe(200);
    }

    private static OutboxEnvelope Envelope(string? traceParent = TraceParent, int attempts = 2)
    {
        return new OutboxEnvelope(
            Guid.Parse("0192b7c4-5d12-7c88-a0e4-9d3b6f1a2c75"),
            Account,
            "EntryRegistered",
            "{\"canary\":\"payload\"}",
            CorrelationId,
            traceParent,
            new DateTimeOffset(2026, 10, 1, 11, 0, 0, TimeSpan.Zero),
            attempts);
    }
}
