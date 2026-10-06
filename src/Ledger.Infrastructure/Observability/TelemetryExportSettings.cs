using Microsoft.Extensions.Configuration;

namespace Ledger.Infrastructure.Observability;

internal sealed record TelemetryExportSettings(bool Traces, bool Metrics)
{
    public const string GeneralEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string TracesEndpointKey = "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT";
    public const string MetricsEndpointKey = "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT";
    public const string TracesExporterKey = "OTEL_TRACES_EXPORTER";
    public const string MetricsExporterKey = "OTEL_METRICS_EXPORTER";

    private const string DisabledExporter = "none";

    public static TelemetryExportSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return From(key => configuration[key]);
    }

    public static TelemetryExportSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static TelemetryExportSettings From(Func<string, string?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        return new TelemetryExportSettings(
            IsEnabled(lookup, TracesEndpointKey, TracesExporterKey),
            IsEnabled(lookup, MetricsEndpointKey, MetricsExporterKey));
    }

    private static bool IsEnabled(Func<string, string?> lookup, string signalEndpointKey, string exporterKey)
    {
        if (string.Equals(lookup(exporterKey), DisabledExporter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(lookup(GeneralEndpointKey))
               || !string.IsNullOrWhiteSpace(lookup(signalEndpointKey));
    }
}
