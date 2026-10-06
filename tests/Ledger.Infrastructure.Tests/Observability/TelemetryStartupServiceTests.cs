using Ledger.Application.Tests.Support;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class TelemetryStartupServiceTests
{
    [Theory]
    [InlineData(true, true, "True", "True")]
    [InlineData(false, false, "False", "False")]
    [InlineData(true, false, "True", "False")]
    public async Task StartAsync_LogsWhichSignalsAreExported(bool traces, bool metrics, string tracesText, string metricsText)
    {
        var logger = new CapturingLogger<TelemetryStartupService>();
        var service = new TelemetryStartupService(new TelemetryExportSettings(traces, metrics), logger);

        await service.StartAsync(CancellationToken.None);

        var entry = logger.Single(9201);
        entry.Level.ShouldBe(LogLevel.Information);
        entry.EventId.Name.ShouldBe("TelemetryExportConfigured");
        entry.Properties["TracesExport"].ShouldBe(tracesText);
        entry.Properties["MetricsExport"].ShouldBe(metricsText);
        entry.Properties.Keys.Where(key => !key.StartsWith('{')).Order().ShouldBe(["MetricsExport", "TracesExport"]);
    }

    [Fact]
    public async Task StopAsync_DoesNothing()
    {
        var logger = new CapturingLogger<TelemetryStartupService>();
        var service = new TelemetryStartupService(new TelemetryExportSettings(false, false), logger);

        await service.StopAsync(CancellationToken.None);

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void AddLedgerTelemetry_RegistersTheStartupServiceAsAHostedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Testing"));

        services.AddLedgerTelemetry("ledger-api", configuration: new ConfigurationBuilder().Build());

        services.Count(descriptor => descriptor.ServiceType == typeof(IHostedService)
                                     && descriptor.ImplementationType == typeof(TelemetryStartupService))
            .ShouldBe(1);
    }
}
