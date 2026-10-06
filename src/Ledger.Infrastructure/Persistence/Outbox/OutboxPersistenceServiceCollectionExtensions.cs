using Ledger.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence.Outbox;

internal static class OutboxPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerOutboxQueue(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = services.AddOptions<IdempotencyPruneOptions>()
            .Bind(configuration.GetSection(IdempotencyPruneOptions.SectionName));

        if (validateOnStart)
        {
            options.ValidateOnStart();
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<IdempotencyPruneOptions>, IdempotencyPruneOptionsValidator>());

        services.TryAddSingleton<IOutboxQueue, PostgresOutboxQueue>();
        services.TryAddSingleton<IIdempotencyKeyPruner, PostgresIdempotencyKeyPruner>();
        services.TryAddSingleton(provider =>
            provider.GetRequiredService<IOptions<IdempotencyPruneOptions>>().Value.ToSettings());

        return services;
    }
}
