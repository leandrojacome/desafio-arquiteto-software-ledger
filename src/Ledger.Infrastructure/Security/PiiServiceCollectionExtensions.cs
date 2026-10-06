using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Security;

internal static class PiiServiceCollectionExtensions
{
    private const string KeysHealthCheckName = "keys";

    public static IServiceCollection AddLedgerPii(
        this IServiceCollection services,
        IConfiguration configuration,
        bool registerHealthCheck = false)
    {
        services.AddOptions<PiiOptions>()
            .Bind(configuration.GetSection(PiiOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PiiOptions>, PiiOptionsValidator>());
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<PiiOptions>>().Value.Rewrap.ToSettings());
        services.TryAddSingleton<IKeySetSource, DirectoryKeySetSource>();
        services.TryAddSingleton<AesGcmDocumentCipher>();
        services.TryAddSingleton(CreateKeyProvider);
        services.TryAddSingleton<IHolderDocumentProtector, HolderDocumentProtector>();
        services.AddHostedService<KeyProviderStartupGuard>();

        if (registerHealthCheck)
        {
            services.AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    KeysHealthCheckName,
                    provider => new KeyProviderHealthCheck(provider.GetRequiredService<IKeyProvider>()),
                    HealthStatus.Degraded,
                    [HealthCheckTags.Ready]));
        }

        return services;
    }

    private static IKeyProvider CreateKeyProvider(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<PiiOptions>>().Value;

        return options.Provider == PiiProvider.Directory
            ? ActivatorUtilities.CreateInstance<ReloadingKeyProvider>(provider)
            : ActivatorUtilities.CreateInstance<ConfigurationKeyProvider>(provider);
    }
}
