using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Ledger.Api.IntegrationTests.Observability;

[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed class EntryTelemetryCompositionTests
{
    private static readonly Type[] TelemetryPorts =
    [
        typeof(IEntryTelemetry),
        typeof(IReadTelemetry),
        typeof(ISecurityTelemetry),
        typeof(IOutboxTelemetry),
        typeof(IIntegrityTelemetry)
    ];

    private static readonly Type[] Implementations =
    [
        typeof(EntryTelemetry),
        typeof(ReadTelemetry),
        typeof(SecurityTelemetry),
        typeof(OutboxTelemetry),
        typeof(IntegrityTelemetry)
    ];

    [Fact]
    public void TheApiHost_ResolvesTheRealTelemetryPortsAsSingletons()
    {
        using var clean = OtelEnvironment.Clean();
        using var factory = new ObservabilityApiFactory(new CapturingLogSink());

        for (var index = 0; index < TelemetryPorts.Length; index++)
        {
            var first = factory.Services.GetRequiredService(TelemetryPorts[index]);

            first.ShouldBeOfType(Implementations[index]);
            first.ShouldBeSameAs(factory.Services.GetRequiredService(TelemetryPorts[index]));
        }
    }

    [Fact]
    public void TheWorkerHost_ResolvesTheRealTelemetryPortsAsSingletons()
    {
        using var clean = OtelEnvironment.Clean();
        using var factory = new WorkerFactory();

        for (var index = 0; index < TelemetryPorts.Length; index++)
        {
            var first = factory.Services.GetRequiredService(TelemetryPorts[index]);

            first.ShouldBeOfType(Implementations[index]);
            first.ShouldBeSameAs(factory.Services.GetRequiredService(TelemetryPorts[index]));
        }
    }

    [Fact]
    public void ARecordedEntry_ReachesTheSdkPipelineWithTheDocumentedNameUnitAndBoundaries()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingMetricExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            configureServices: services => services.ConfigureOpenTelemetryMeterProvider(
                metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue))));
        var telemetry = factory.Services.GetRequiredService<IEntryTelemetry>();

        using (var operation = telemetry.Begin("credit"))
        {
            operation.Recorded("credit");
        }

        factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        exporter.Metrics.Last(metric => metric.Name == "ledger.entries.recorded").Unit.ShouldBe("{entry}");
        var duration = exporter.Metrics.Last(metric => metric.Name == "ledger.entry.duration");
        duration.Unit.ShouldBe("s");
        duration.Boundaries.ShouldBe([0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5]);
    }

    [Fact]
    public void TheIntegrityRunOfTheHost_ReachesTheSdkPipelineAsAnObservableGauge()
    {
        using var clean = OtelEnvironment.Clean();
        using var exporter = new CapturingMetricExporter();
        using var factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            configureServices: services => services.ConfigureOpenTelemetryMeterProvider(
                metrics => metrics.AddReader(new PeriodicExportingMetricReader(exporter, int.MaxValue))));
        var telemetry = factory.Services.GetRequiredService<IIntegrityTelemetry>();

        using (var run = telemetry.BeginRun(IntegrityMode.Recent))
        {
            run.Completed(true);
        }

        factory.Services.GetRequiredService<MeterProvider>().ForceFlush(5000).ShouldBeTrue();

        exporter.Metrics.ShouldContain(metric => metric.Name == "ledger.integrity.last.success.timestamp" && metric.Unit == "s");
        exporter.Metrics.ShouldContain(metric => metric.Name == "ledger.integrity.check.runs");
    }
}
