namespace Ledger.Api.IntegrationTests.Observability;

internal static class OtelEnvironment
{
    public const string Collection = "OpenTelemetryEnvironment";

    public static readonly string[] Variables =
    [
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_PROTOCOL",
        "OTEL_TRACES_EXPORTER",
        "OTEL_METRICS_EXPORTER",
        "OTEL_TRACES_SAMPLER",
        "OTEL_TRACES_SAMPLER_ARG",
        "OTEL_SERVICE_NAME",
        "OTEL_RESOURCE_ATTRIBUTES"
    ];

    public static EnvironmentVariableScope Clean() => EnvironmentVariableScope.Cleared(Variables);

    public static EnvironmentVariableScope Exporting(string endpoint)
    {
        var values = Variables.ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        values["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint;
        values["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf";

        return new EnvironmentVariableScope(values);
    }
}
