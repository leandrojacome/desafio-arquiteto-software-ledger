using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
[Collection("OpenTelemetryProviders")]
public sealed class TelemetryProviderTests
{
    private static ServiceProvider Build(
        IReadOnlyDictionary<string, string?> settings,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null,
        string environment = "Testing")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddLedgerTelemetry("ledger-api", tracing, metrics, configuration);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> NoSettings() => [];

    [Fact]
    public void Telemetry_SourceAndMeter_AreCalledLedger()
    {
        Telemetry.Source.Name.ShouldBe("Ledger");
        Telemetry.Meter.Name.ShouldBe("Ledger");
        Telemetry.Name.ShouldBe("Ledger");
    }

    [Fact]
    public void Resource_CarriesTheServiceNameTheVersionAndTheEnvironment()
    {
        using var provider = Build(NoSettings(), environment: "Staging");

        var attributes = provider.GetRequiredService<TracerProvider>().GetResource().Attributes
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        attributes["service.name"].ShouldBe("ledger-api");
        attributes["service.version"].ShouldBe(BuildInfo.Version);
        attributes["deployment.environment"].ShouldBe("Staging");
    }

    [Fact]
    public void Resource_ServiceNameFromTheStandardVariable_WinsOverTheCode()
    {
        using var provider = Build(new Dictionary<string, string?> { ["OTEL_SERVICE_NAME"] = "ledger-api-canary" });

        var attributes = provider.GetRequiredService<TracerProvider>().GetResource().Attributes
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        attributes["service.name"].ShouldBe("ledger-api-canary");
    }

    [Fact]
    public void Resource_AttributesFromTheStandardVariable_WinOverTheDefaults()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["OTEL_RESOURCE_ATTRIBUTES"] = "deployment.environment=production,service.version=9.9.9"
            });

        var attributes = provider.GetRequiredService<TracerProvider>().GetResource().Attributes
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        attributes["deployment.environment"].ShouldBe("production");
        attributes["service.version"].ShouldBe("9.9.9");
    }

    [Theory]
    [InlineData("http://otel-collector:4317", true, true)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    public void ExportSettings_FollowTheGeneralEndpointVariable(string endpoint, bool traces, bool metrics)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint })
            .Build();

        var settings = TelemetryExportSettings.From(configuration);

        settings.Traces.ShouldBe(traces);
        settings.Metrics.ShouldBe(metrics);
    }

    [Fact]
    public void ExportSettings_WithoutAnyVariable_AreOffByDefault()
    {
        TelemetryExportSettings.From(new ConfigurationBuilder().Build()).ShouldBe(new TelemetryExportSettings(false, false));
    }

    [Fact]
    public void ExportSettings_AcceptPerSignalEndpoints()
    {
        var onlyTraces = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = "http://t:4317" })
            .Build();
        var onlyMetrics = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] = "http://m:4317" })
            .Build();

        TelemetryExportSettings.From(onlyTraces).ShouldBe(new TelemetryExportSettings(true, false));
        TelemetryExportSettings.From(onlyMetrics).ShouldBe(new TelemetryExportSettings(false, true));
    }

    [Fact]
    public void ExportSettings_FromTheEnvironmentLookup_FollowTheSameRules()
    {
        var variables = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://otel-collector:4317",
            ["OTEL_METRICS_EXPORTER"] = "none"
        };

        var settings = TelemetryExportSettings.From(key => variables.GetValueOrDefault(key));

        settings.ShouldBe(new TelemetryExportSettings(true, false));
    }

    [Fact]
    public void ExportSettings_ExporterSetToNone_TurnsTheSignalOffEvenWithAnEndpoint()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://otel-collector:4317",
                    ["OTEL_TRACES_EXPORTER"] = "none",
                    ["OTEL_METRICS_EXPORTER"] = "NONE"
                })
            .Build();

        TelemetryExportSettings.From(configuration).ShouldBe(new TelemetryExportSettings(false, false));
    }

    [Fact]
    public void ExportSettings_FromTheContainer_ReflectTheConfiguration()
    {
        using var off = Build(NoSettings());
        using var on = Build(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://otel:4317" });

        off.GetRequiredService<TelemetryExportSettings>().Traces.ShouldBeFalse();
        on.GetRequiredService<TelemetryExportSettings>().Traces.ShouldBeTrue();
        on.GetRequiredService<TelemetryExportSettings>().Metrics.ShouldBeTrue();
    }

    [Fact]
    public void Tracing_WithTheEndpointDefined_ReallyExportsToTheCollector()
    {
        using var collector = new FakeOtlpCollector();
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
            });
        var tracer = provider.GetRequiredService<TracerProvider>();

        using (Telemetry.Source.StartActivity("test.export"))
        {
        }

        tracer.ForceFlush(5000).ShouldBeTrue();

        Eventually(() => collector.RequestLines.Any(line => line.StartsWith("POST /v1/traces", StringComparison.Ordinal)))
            .ShouldBeTrue(string.Join(" | ", collector.RequestLines));
    }

    [Fact]
    public void Metrics_WithTheEndpointDefined_ReallyExportsToTheCollector()
    {
        using var collector = new FakeOtlpCollector();
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = collector.Endpoint,
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
            });
        var meter = provider.GetRequiredService<MeterProvider>();
        using var source = new Meter("Ledger");
        source.CreateCounter<long>("test.export.counter").Add(1);

        meter.ForceFlush(5000).ShouldBeTrue();

        Eventually(() => collector.RequestLines.Any(line => line.StartsWith("POST /v1/metrics", StringComparison.Ordinal)))
            .ShouldBeTrue(string.Join(" | ", collector.RequestLines));
    }

    [Fact]
    public void Tracing_WithoutAnEndpoint_ExportsNothingAndNeverBlocks()
    {
        using var exporter = new CapturingActivityExporter();
        using var provider = Build(
            NoSettings(),
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
        var tracer = provider.GetRequiredService<TracerProvider>();
        var watch = Stopwatch.StartNew();

        for (var index = 0; index < 500; index++)
        {
            using (Telemetry.Source.StartActivity("test.no-export"))
            {
            }
        }

        tracer.ForceFlush(5000).ShouldBeTrue();
        watch.Stop();

        provider.GetRequiredService<TelemetryExportSettings>().Traces.ShouldBeFalse();
        exporter.Names.Count(name => name == "test.no-export").ShouldBe(500);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Tracing_WithADeadCollector_DoesNotDelayTheCaller()
    {
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf"
            });
        provider.GetRequiredService<TracerProvider>();
        var watch = Stopwatch.StartNew();

        for (var index = 0; index < 1000; index++)
        {
            using (Telemetry.Source.StartActivity("test.dead-collector"))
            {
            }
        }

        watch.Stop();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Sampler_FromTheEnvironmentAlwaysOff_SamplesNothing()
    {
        using var exporter = new CapturingActivityExporter();
        using var provider = Build(
            new Dictionary<string, string?> { ["OTEL_TRACES_SAMPLER"] = "always_off" },
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
        provider.GetRequiredService<TracerProvider>();

        for (var index = 0; index < 200; index++)
        {
            using var activity = Telemetry.Source.StartActivity("test.always-off");
            (activity?.Recorded ?? false).ShouldBeFalse();
            (activity?.IsAllDataRequested ?? false).ShouldBeFalse();
        }

        exporter.Names.Where(name => name == "test.always-off").ShouldBeEmpty();
    }

    [Fact]
    public void Sampler_FromTheEnvironmentWithARatio_SamplesAboutThatFraction()
    {
        const int requests = 5000;
        using var exporter = new CapturingActivityExporter();
        using var provider = Build(
            new Dictionary<string, string?>
            {
                ["OTEL_TRACES_SAMPLER"] = "parentbased_traceidratio",
                ["OTEL_TRACES_SAMPLER_ARG"] = "0.1"
            },
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
        provider.GetRequiredService<TracerProvider>();

        for (var index = 0; index < requests; index++)
        {
            using (Telemetry.Source.StartActivity("test.ratio"))
            {
            }
        }

        var sampled = exporter.Names.Count(name => name == "test.ratio");
        var fraction = sampled / (double)requests;

        fraction.ShouldBeInRange(0.07, 0.13);
    }

    [Fact]
    public void Sampler_WithoutTheVariable_SamplesEverything()
    {
        using var exporter = new CapturingActivityExporter();
        using var provider = Build(
            NoSettings(),
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
        provider.GetRequiredService<TracerProvider>();

        for (var index = 0; index < 300; index++)
        {
            using (Telemetry.Source.StartActivity("test.all"))
            {
            }
        }

        exporter.Names.Count(name => name == "test.all").ShouldBe(300);
    }

    [Fact]
    public void Metrics_HistogramsUseTheExplicitBoundariesOfTheDocumentation()
    {
        using var exporter = new CapturingMetricExporter();
        using var provider = Build(
            NoSettings(),
            metrics: metrics =>
            {
                metrics.AddMeter("Test.Http");
                metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue));
            });
        var meterProvider = provider.GetRequiredService<MeterProvider>();
        using var ledger = new Meter("Ledger");
        using var http = new Meter("Test.Http");

        http.CreateHistogram<double>("http.server.request.duration", "s").Record(0.12);
        ledger.CreateHistogram<double>("ledger.entry.duration", "s").Record(0.01);
        ledger.CreateHistogram<double>("ledger.balance.query.duration", "s").Record(0.01);
        ledger.CreateHistogram<double>("ledger.db.command.duration", "s").Record(0.01);
        ledger.CreateHistogram<double>("outbox.publish.duration", "s").Record(0.01);
        ledger.CreateHistogram<double>("ledger.integrity.check.duration", "s").Record(0.01);
        meterProvider.ForceFlush(5000).ShouldBeTrue();

        BoundsOf(exporter, "http.server.request.duration")
            .ShouldBe([0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5, 5]);
        BoundsOf(exporter, "ledger.entry.duration")
            .ShouldBe([0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5]);
        BoundsOf(exporter, "ledger.balance.query.duration")
            .ShouldBe([0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5]);
        BoundsOf(exporter, "ledger.db.command.duration")
            .ShouldBe([0.001, 0.002, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1]);
        BoundsOf(exporter, "outbox.publish.duration")
            .ShouldBe([0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5]);
        BoundsOf(exporter, "ledger.integrity.check.duration")
            .ShouldBe([0.1, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 900, 1800, 3600]);
    }

    [Fact]
    public void Metrics_TheBoundariesIncludeThePointsTheSlosNeed()
    {
        HistogramBoundaries.HttpRequest.ShouldContain(0.15);
        HistogramBoundaries.HttpRequest.ShouldContain(0.05);
        HistogramBoundaries.UseCase.ShouldContain(0.15);
        HistogramBoundaries.UseCase.ShouldContain(0.05);
    }

    [Fact]
    public void Metrics_RegisterTheRuntimeInstrumentationAndTheNpgsqlMeter()
    {
        using var exporter = new CapturingMetricExporter();
        using var provider = Build(
            NoSettings(),
            metrics: metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue)));
        var meterProvider = provider.GetRequiredService<MeterProvider>();
        using var npgsql = new Meter("Npgsql");
        npgsql.CreateCounter<long>("db.client.test.counter").Add(1);

        GC.Collect();
        meterProvider.ForceFlush(5000).ShouldBeTrue();

        exporter.Metrics.ShouldContain(metric => metric.Name.StartsWith("dotnet.", StringComparison.Ordinal));
        exporter.Metrics.ShouldContain(metric => metric.Name == "db.client.test.counter");
    }

    [Fact]
    public void Tracing_RegistersTheLedgerSource()
    {
        using var exporter = new CapturingActivityExporter();
        using var provider = Build(
            NoSettings(),
            tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
        provider.GetRequiredService<TracerProvider>();
        using var other = new ActivitySource("Some.Other.Source");

        using (Telemetry.Source.StartActivity("ledger.registered"))
        {
        }

        using (other.StartActivity("other.ignored"))
        {
        }

        exporter.Names.ShouldContain("ledger.registered");
        exporter.Names.ShouldNotContain("other.ignored");
    }

    private static IReadOnlyList<double> BoundsOf(CapturingMetricExporter exporter, string name)
    {
        return exporter.Metrics.Last(metric => metric.Name == name).Boundaries;
    }

    private static bool Eventually(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();

        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            if (condition())
            {
                return true;
            }

            SpinWait.SpinUntil(() => false, TimeSpan.FromMilliseconds(50));
        }

        return condition();
    }
}
