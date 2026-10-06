using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure.Resilience;

internal static class FailureClassificationServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerFailureClassification(this IServiceCollection services)
    {
        services.TryAddSingleton<PostgresTransientFailureClassifier>();
        services.TryAddSingleton<KeyProviderFailureClassifier>();
        services.TryAddSingleton<ITransientFailureClassifier, CompositeTransientFailureClassifier>();

        return services;
    }
}
