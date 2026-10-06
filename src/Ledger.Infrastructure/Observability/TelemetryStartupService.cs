using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Observability;

internal sealed class TelemetryStartupService(
    TelemetryExportSettings settings,
    ILogger<TelemetryStartupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        TelemetryLog.ExportConfigured(logger, settings.Traces, settings.Metrics);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
