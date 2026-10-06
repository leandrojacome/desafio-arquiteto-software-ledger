using Serilog;
using Serilog.Formatting.Compact;

namespace Ledger.Infrastructure.Observability;

internal static class LoggerConfigurationExtensions
{
    public const string ServiceProperty = "Service";
    public const string VersionProperty = "Version";
    public const string EnvironmentProperty = "Environment";

    public static LoggerConfiguration WithLedgerIdentity(
        this LoggerConfiguration configuration,
        string serviceName,
        string environmentName)
    {
        return configuration
            .Enrich.WithProperty(ServiceProperty, serviceName)
            .Enrich.WithProperty(VersionProperty, BuildInfo.Version)
            .Enrich.WithProperty(EnvironmentProperty, environmentName);
    }

    public static LoggerConfiguration WithLedgerEnrichers(this LoggerConfiguration configuration)
    {
        return configuration
            .Enrich.FromLogContext()
            .Enrich.With<TraceContextEnricher>()
            .Enrich.With<CorrelationIdEnricher>();
    }

    public static LoggerConfiguration WithLedgerProtection(this LoggerConfiguration configuration)
    {
        return configuration
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .Enrich.With<SensitiveValueMaskingEnricher>();
    }

    public static LoggerConfiguration WriteLedgerJsonToConsole(this LoggerConfiguration configuration)
    {
        return configuration.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
}
