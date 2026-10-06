using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ledger.Infrastructure.Observability;

public static class TelemetryServiceCollectionExtensions
{
    private const string ServiceNameKey = "OTEL_SERVICE_NAME";
    private const string DeploymentEnvironmentAttribute = "deployment.environment";

    public static IServiceCollection AddLedgerTelemetry(
        this IServiceCollection services,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null,
        Action<MeterProviderBuilder>? configureMetrics = null,
        IConfiguration? configuration = null)
    {
        var export = configuration is null
            ? TelemetryExportSettings.FromEnvironment()
            : TelemetryExportSettings.From(configuration);

        services.AddSingleton(export);
        services.AddHostedService<TelemetryStartupService>();

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Telemetry.Name)
                    .AddPostgresTracing()
                    .AddHttpClientInstrumentation();

                configureTracing?.Invoke(tracing);

                if (export.Traces)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(Telemetry.Name)
                    .AddPostgresMetrics()
                    .AddRuntimeInstrumentation()
                    .AddLedgerViews();

                configureMetrics?.Invoke(metrics);

                if (export.Metrics)
                {
                    metrics.AddOtlpExporter();
                }
            });

        services.ConfigureOpenTelemetryTracerProvider((serviceProvider, tracing) =>
            tracing.ConfigureResource(resource => ConfigureResource(resource, serviceProvider, serviceName)));

        services.ConfigureOpenTelemetryMeterProvider((serviceProvider, metrics) =>
            metrics.ConfigureResource(resource => ConfigureResource(resource, serviceProvider, serviceName)));

        return services;
    }

    private static void ConfigureResource(ResourceBuilder resource, IServiceProvider serviceProvider, string serviceName)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
        var configuredName = configuration[ServiceNameKey];
        var effectiveName = string.IsNullOrWhiteSpace(configuredName) ? serviceName : configuredName;

        resource
            .AddService(effectiveName, serviceVersion: BuildInfo.Version)
            .AddAttributes([new KeyValuePair<string, object>(DeploymentEnvironmentAttribute, environment.EnvironmentName)])
            .AddEnvironmentVariableDetector();
    }
}
