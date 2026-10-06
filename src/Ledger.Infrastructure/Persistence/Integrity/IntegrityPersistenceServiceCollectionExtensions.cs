using Ledger.Application.Integrity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure.Persistence.Integrity;

internal static class IntegrityPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerIntegrity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIntegritySessions, PostgresIntegritySessions>();

        return services;
    }
}
