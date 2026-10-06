using Ledger.Application.Abstractions;
using Ledger.Application.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure.Observability;

internal static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerObservability(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(TelemetrySources.Default);
        services.TryAddSingleton<ClientLabels>();
        services.TryAddSingleton<OutboxStatsHolder>();
        services.TryAddSingleton<IntegritySuccessTracker>();
        services.TryAddSingleton<WorkerLoopSuccesses>();
        services.TryAddSingleton<KeyUsageHolder>();
        services.TryAddSingleton<LedgerMeters>();
        services.TryAddSingleton<DbTelemetry>();
        services.TryAddSingleton<IEntryTelemetry, EntryTelemetry>();
        services.TryAddSingleton<IReadTelemetry, ReadTelemetry>();
        services.TryAddSingleton<ISecurityTelemetry, SecurityTelemetry>();
        services.TryAddSingleton<IOutboxTelemetry, OutboxTelemetry>();
        services.TryAddSingleton<IIntegrityTelemetry, IntegrityTelemetry>();
        services.TryAddSingleton<IWorkerLoopTelemetry, WorkerLoopTelemetry>();

        return services;
    }
}
