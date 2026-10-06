using System.Diagnostics;
using Ledger.Application.Outbox;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Serilog;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class OutboxTracingTests : IDisposable
{
    private const string OriginTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string OriginSpanId = "00f067aa0ba902b7";
    private const string TraceParent = "00-" + OriginTraceId + "-" + OriginSpanId + "-01";
    private const string CorrelationId = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";

    private static readonly AccountId Account =
        AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;

    private readonly TestTelemetry _telemetry = new();

    public void Dispose()
    {
        _telemetry.Dispose();
    }

    [Fact]
    public void Publish_StartsANewTraceEvenInsideThePollSpan()
    {
        using var poll = _telemetry.OutboxPort.BeginPoll();
        var pollSpan = Activity.Current.ShouldNotBeNull();

        using (_telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            var publish = Activity.Current.ShouldNotBeNull();

            publish.Parent.ShouldBeNull();
            publish.ParentId.ShouldBeNull();
            publish.TraceId.ShouldNotBe(pollSpan.TraceId);
        }
    }

    [Fact]
    public void Publish_IsAProducerSpanWithTheMessagingAttributes()
    {
        using (var operation = _telemetry.OutboxPort.BeginPublish(Envelope(attempts: 3)))
        {
            operation.Confirmed();
        }

        var span = _telemetry.Capture.SingleActivity("outbox.publish");
        span.Kind.ShouldBe(ActivityKind.Producer);
        span.GetTagItem("messaging.system").ShouldBe("rabbitmq");
        span.GetTagItem("messaging.destination.name").ShouldBe("ledger.events");
        span.GetTagItem("ledger.outbox.attempt").ShouldBe(3);
        span.GetTagItem("ledger.outcome").ShouldBe("confirmed");
        span.GetTagItem("ledger.correlation_id").ShouldBe(CorrelationId);
        span.Status.ShouldNotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public void Publish_WithATraceparent_LinksToTheOriginalContext()
    {
        using (_telemetry.OutboxPort.BeginPublish(Envelope()))
        {
        }

        var link = _telemetry.Capture.SingleActivity("outbox.publish").Links.ShouldHaveSingleItem();
        link.Context.TraceId.ToHexString().ShouldBe(OriginTraceId);
        link.Context.SpanId.ToHexString().ShouldBe(OriginSpanId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-0000000000000000-01")]
    public void Publish_WithoutAUsableTraceparent_HasNoLink(string? traceParent)
    {
        using (_telemetry.OutboxPort.BeginPublish(Envelope(traceParent)))
        {
        }

        _telemetry.Capture.SingleActivity("outbox.publish").Links.ShouldBeEmpty();
    }

    [Fact]
    public void Publish_Failed_MarksTheOutcomeAndTheStatus()
    {
        using (var operation = _telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            operation.Failed(PublishFailureReason.Timeout);
        }

        var span = _telemetry.Capture.SingleActivity("outbox.publish");
        span.GetTagItem("ledger.outcome").ShouldBe("failed");
        span.Status.ShouldBe(ActivityStatusCode.Error);
    }

    [Fact]
    public void Publish_OnDispose_RestoresTheSpanThatWasCurrent()
    {
        using var poll = _telemetry.OutboxPort.BeginPoll();
        var pollSpan = Activity.Current;

        var operation = _telemetry.OutboxPort.BeginPublish(Envelope());
        operation.Dispose();

        Activity.Current.ShouldBe(pollSpan);
    }

    [Fact]
    public void Publish_DisposedTwice_FinishesTheSpanOnce()
    {
        var operation = _telemetry.OutboxPort.BeginPublish(Envelope());

        operation.Dispose();
        operation.Dispose();

        _telemetry.Capture.Activities.Count(activity => activity.OperationName == "outbox.publish").ShouldBe(1);
    }

    [Fact]
    public async Task Publish_ForManyMessagesInParallel_EachOneIsItsOwnRootTrace()
    {
        using var poll = _telemetry.OutboxPort.BeginPoll();
        var pollSpan = Activity.Current.ShouldNotBeNull();
        var pollTrace = pollSpan.TraceId;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => Task.Run(async () =>
        {
            using var operation = _telemetry.OutboxPort.BeginPublish(Envelope(attempts: index));
            await Task.Yield();
            operation.Confirmed();
        })));

        var spans = _telemetry.Capture.Activities.Where(activity => activity.OperationName == "outbox.publish").ToList();
        spans.Count.ShouldBe(20);
        spans.Select(span => span.TraceId).Distinct().Count().ShouldBe(20);
        spans.ShouldAllBe(span => span.Parent == null && span.TraceId != pollTrace);
        Activity.Current.ShouldBe(pollSpan);
    }

    [Fact]
    public void Publish_LogsEmittedInsideTheSpan_CarryTheCorrelationIdOfTheMessage()
    {
        var sink = new CollectingLogSink();
        using var logger = new LoggerConfiguration().WithLedgerEnrichers().WriteTo.Sink(sink).CreateLogger();

        using (_telemetry.OutboxPort.BeginPublish(Envelope()))
        {
            logger.Warning("publish failed");
        }

        logger.Warning("after the span");

        CollectingLogSink.Property(sink.Events[0], "CorrelationId").ShouldBe($"\"{CorrelationId}\"");
        CollectingLogSink.Property(sink.Events[1], "CorrelationId").ShouldBeEmpty();
    }

    [Fact]
    public void Poll_RecordsTheBatchSizeAndKeepsTheCurrentTraceAsItsParent()
    {
        using var request = TestActivities.Start("worker.loop");

        using (var operation = _telemetry.OutboxPort.BeginPoll())
        {
            operation.BatchSize(200);
        }

        var span = _telemetry.Capture.SingleActivity("outbox.poll");
        span.Parent.ShouldBe(request);
        span.GetTagItem("outbox.batch_size").ShouldBe(200);
    }

    private static OutboxEnvelope Envelope(string? traceParent = TraceParent, int attempts = 1)
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
