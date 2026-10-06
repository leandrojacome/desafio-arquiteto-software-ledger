using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Health;

public static class WorkerHealthServiceCollectionExtensions
{
    private const string HeartbeatsName = "heartbeats";
    private const string OutboxLagName = "outbox-lag";
    private const string BrokerCircuitName = "broker-circuit";
    private const string RabbitMqName = "rabbitmq";
    private const string ShutdownName = "shutdown";
    private const string KeyUsageName = "key-usage";

    public static IServiceCollection AddLedgerWorkerHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WorkerHealthOptions>()
            .Bind(configuration.GetSection(WorkerHealthOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<WorkerHealthOptions>, WorkerHealthOptionsValidator>());

        services.TryAddSingleton<IRabbitMqConnector, RabbitMqConnector>();
        services.TryAddSingleton<RabbitMqReadinessHealthCheck>();
        services.TryAddSingleton<CachedRabbitMqReadiness>();

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                HeartbeatsName,
                provider => new WorkerLivenessHealthCheck(
                    provider.GetRequiredService<IWorkerHeartbeat>(),
                    provider.GetRequiredService<IOptions<WorkerHealthOptions>>(),
                    provider.GetRequiredService<TimeProvider>()),
                failureStatus: null,
                tags: [WorkerHealthTags.Live]))
            .Add(new HealthCheckRegistration(
                OutboxLagName,
                provider => new OutboxLagHealthCheck(
                    provider.GetRequiredService<OutboxStatsHolder>(),
                    provider.GetRequiredService<IOptions<WorkerHealthOptions>>(),
                    provider.GetRequiredService<TimeProvider>()),
                failureStatus: HealthStatus.Degraded,
                tags: [HealthCheckTags.Ready]))
            .Add(new HealthCheckRegistration(
                KeyUsageName,
                provider => new KeyUsageHealthCheck(provider.GetRequiredService<KeyUsageHolder>()),
                failureStatus: HealthStatus.Degraded,
                tags: [HealthCheckTags.Ready]))
            .Add(new HealthCheckRegistration(
                BrokerCircuitName,
                provider => new BrokerCircuitHealthCheck(provider.GetRequiredService<IEventPublisher>()),
                failureStatus: HealthStatus.Degraded,
                tags: [HealthCheckTags.Ready]))
            .Add(new HealthCheckRegistration(
                RabbitMqName,
                provider => provider.GetRequiredService<CachedRabbitMqReadiness>(),
                failureStatus: HealthStatus.Degraded,
                tags: [HealthCheckTags.Ready]));

        services.PostConfigure<HealthCheckServiceOptions>(AddShutdownWhenMissing);

        return services;
    }

    private static void AddShutdownWhenMissing(HealthCheckServiceOptions options)
    {
        if (options.Registrations.Any(registration => registration.Name == ShutdownName))
        {
            return;
        }

        options.Registrations.Add(new HealthCheckRegistration(
            ShutdownName,
            provider => new ShutdownHealthCheck(provider.GetRequiredService<IHostApplicationLifetime>()),
            failureStatus: null,
            tags: [HealthCheckTags.Ready]));
    }
}
