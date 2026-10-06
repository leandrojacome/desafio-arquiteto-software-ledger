using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Observability;

internal static partial class TelemetryLog
{
    [LoggerMessage(
        EventId = 9201,
        EventName = "TelemetryExportConfigured",
        Level = LogLevel.Information,
        Message = "Telemetry export configured: traces {TracesExport}, metrics {MetricsExport}")]
    public static partial void ExportConfigured(ILogger logger, bool tracesExport, bool metricsExport);
}
