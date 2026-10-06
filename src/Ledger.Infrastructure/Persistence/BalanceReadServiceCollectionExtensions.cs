using Ledger.Application.Balances;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal static class BalanceReadServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerBalanceSettings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BalanceReadOptions>()
            .Bind(configuration.GetSection(BalanceReadOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BalanceReadOptions>, BalanceReadOptionsValidator>());
        services.TryAddSingleton(provider => new BalanceReadSettings(
            TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<BalanceReadOptions>>().Value.SettlingWindowSeconds)));

        return services;
    }
}
