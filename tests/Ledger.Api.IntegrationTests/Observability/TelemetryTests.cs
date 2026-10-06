using System.Collections.Concurrent;
using System.Diagnostics;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed class TelemetryTests
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public void Telemetry_SourceAndMeter_AreCalledLedger()
    {
        Telemetry.Source.Name.ShouldBe("Ledger");
        Telemetry.Meter.Name.ShouldBe("Ledger");
    }

    [Fact]
    public async Task Request_CarriesTheCorrelationIdAsATagOfTheRequestActivity()
    {
        using var clean = OtelEnvironment.Clean();
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());
        var correlation = Guid.NewGuid().ToString("N");
        var tagged = new ConcurrentBag<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("ledger.correlation_id") is string value)
                {
                    tagged.Add(value);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationHeader, correlation);

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.EnsureSuccessStatusCode();
        tagged.ShouldContain(correlation);
    }

    [Fact]
    public void Exporter_WithoutTheEndpointVariable_IsNotRegistered()
    {
        using var clean = OtelEnvironment.Clean();
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());

        var settings = factory.Services.GetRequiredService<TelemetryExportSettings>();

        settings.ShouldBe(new TelemetryExportSettings(false, false));
    }

    [Fact]
    public async Task Exporter_WithTheEndpointVariable_IsRegisteredAndSendsTracesAndMetrics()
    {
        using var collector = new FakeOtlpCollector();
        using var environment = OtelEnvironment.Exporting(collector.Endpoint);
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush(5000).ShouldBeTrue();
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        factory.Services.GetRequiredService<TelemetryExportSettings>().ShouldBe(new TelemetryExportSettings(true, true));
        await WaitForAsync(() => collector.RequestLines.Any(line => line.StartsWith("POST /v1/traces", StringComparison.Ordinal)));
        await WaitForAsync(() => collector.RequestLines.Any(line => line.StartsWith("POST /v1/metrics", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HttpServerRequestDuration_UsesTheBoundariesOfTheObservabilityDocument()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingMetricExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            configureServices: services => services.ConfigureOpenTelemetryMeterProvider(
                metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue))));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        var duration = exporter.Metrics.Last(metric => metric.Name == "http.server.request.duration");
        duration.Unit.ShouldBe("s");
        duration.Boundaries.ShouldBe([0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5, 5]);
    }

    [Fact]
    public async Task Metrics_OfTheApi_IncludeTheRuntimeInstrumentation()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingMetricExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            configureServices: services => services.ConfigureOpenTelemetryMeterProvider(
                metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue))));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);
        GC.Collect();
        factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        exporter.Metrics.ShouldContain(metric => metric.Name.StartsWith("dotnet.", StringComparison.Ordinal));
    }

    [Fact]
    public void Resource_OfTheApi_CarriesTheServiceNameTheVersionAndTheEnvironment()
    {
        using var clean = OtelEnvironment.Clean();
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());

        var attributes = factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        attributes["service.name"].ShouldBe("ledger-api");
        attributes["service.version"].ShouldBe(BuildInfo.Version);
        attributes["deployment.environment"].ShouldBe("Testing");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < TimeSpan.FromSeconds(5) && !condition())
        {
            await Task.Delay(50);
        }

        condition().ShouldBeTrue();
    }
}
