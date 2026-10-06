using System.Diagnostics;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class LogEnrichersTests
{
    private const string CorrelationId = "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10";

    private readonly CollectingLogSink _sink = new();

    private Logger Logger() =>
        new LoggerConfiguration()
            .WithLedgerEnrichers()
            .WriteTo.Sink(_sink)
            .CreateLogger();

    private string? Property(string name) =>
        _sink.Events.Single().Properties.TryGetValue(name, out var value)
            ? value.ShouldBeOfType<ScalarValue>().Value as string
            : null;

    [Fact]
    public void CorrelationId_FromTheTagOfTheCurrentActivity_IsAddedToTheEvent()
    {
        using var logger = Logger();
        using var activity = TestActivities.Start("outbox.publish");
        activity.SetTag("ledger.correlation_id", CorrelationId);

        logger.Information("published");

        Property("CorrelationId").ShouldBe(CorrelationId);
    }

    [Fact]
    public void CorrelationId_FromTheTagOfAParentActivity_IsAddedToTheEvent()
    {
        using var logger = Logger();
        using var parent = TestActivities.Start("http.request");
        parent.SetTag("ledger.correlation_id", CorrelationId);
        using var child = TestActivities.Start("ledger.record_entry");

        logger.Information("inside the use case");

        Property("CorrelationId").ShouldBe(CorrelationId);
    }

    [Fact]
    public void CorrelationId_AlreadyInTheLogContext_IsNotReplaced()
    {
        using var logger = Logger();
        using var activity = TestActivities.Start("http.request");
        activity.SetTag("ledger.correlation_id", "from-the-activity-0001");

        using (LogContext.PushProperty("CorrelationId", "from-the-context-0001"))
        {
            logger.Information("inside the middleware scope");
        }

        Property("CorrelationId").ShouldBe("from-the-context-0001");
    }

    [Fact]
    public void CorrelationId_WithoutAnActivityOrTag_IsNotInvented()
    {
        using var logger = Logger();

        logger.Information("outside any request");

        Property("CorrelationId").ShouldBeNull();
    }

    [Fact]
    public async Task CorrelationId_OnConcurrentFlows_NeverLeaksFromOneFlowToTheOther()
    {
        using var logger = Logger();
        using var barrier = new Barrier(2);

        async Task<string> RunAsync(string correlation)
        {
            using var activity = TestActivities.Start("http.request");
            activity.SetTag("ledger.correlation_id", correlation);
            barrier.SignalAndWait(TimeSpan.FromSeconds(5));
            await Task.Yield();
            logger.Information("flow {Flow}", correlation);

            return correlation;
        }

        await Task.WhenAll(Task.Run(() => RunAsync("flow-one-correlation-1")), Task.Run(() => RunAsync("flow-two-correlation-2")));

        foreach (var logEvent in _sink.Events)
        {
            var flow = CollectingLogSink.Property(logEvent, "Flow").Trim('"');
            CollectingLogSink.Property(logEvent, "CorrelationId").Trim('"').ShouldBe(flow);
        }
    }

    [Fact]
    public void TraceContext_FromTheCurrentActivity_AddsTraceIdAndSpanId()
    {
        using var logger = Logger();
        using var activity = TestActivities.Start("http.request");

        logger.Information("with a trace");

        Property("TraceId").ShouldBe(activity.TraceId.ToHexString());
        Property("SpanId").ShouldBe(activity.SpanId.ToHexString());
    }

    [Fact]
    public void TraceContext_WithoutAnActivity_AddsNothing()
    {
        using var logger = Logger();

        logger.Information("without a trace");

        Property("TraceId").ShouldBeNull();
        Property("SpanId").ShouldBeNull();
    }

    [Fact]
    public void TraceContext_OfADroppedSpan_StillCarriesTheTraceId()
    {
        using var logger = Logger();
        using var source = new ActivitySource("test.dropped");
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData
        };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("dropped");

        logger.Error("an error under a dropped span");

        activity.ShouldNotBeNull();
        activity.IsAllDataRequested.ShouldBeFalse();
        Property("TraceId").ShouldBe(activity.TraceId.ToHexString());
    }
}
