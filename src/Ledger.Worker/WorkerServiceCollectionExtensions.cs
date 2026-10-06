using Ledger.Infrastructure.Health;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerWorker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IntegrityOptions>()
            .Bind(configuration.GetSection(IntegrityOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<IntegrityOptions>, IntegrityOptionsValidator>());

        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>());

        services.AddLedgerWorkerHealthChecks(configuration);

        services.TryAddSingleton<WorkerLoopHost>();

        services.AddHostedService<IntegrityCheckService>();
        services.AddHostedService<OutboxPruneService>();
        services.AddHostedService<IdempotencyPruneService>();
        services.AddHostedService<OutboxMeasurementService>();
        services.AddHostedService<OutboxPublisherService>();
        services.AddHostedService<KeyRewrapService>();

        return services;
    }
}
