using System.ComponentModel.DataAnnotations;
using Ledger.Application.Balances;
using Ledger.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal sealed class BalanceReadOptions
{
    public const string SectionName = "Ledger:Balance";

    [Range(1, 60)] public int SettlingWindowSeconds { get; init; } = 5;
}

internal sealed class BalanceReadOptionsValidator : IValidateOptions<BalanceReadOptions>
{
    public ValidateOptionsResult Validate(string? name, BalanceReadOptions options)
    {
        var failures = new List<string>();

        OptionsValidation.Collect(options, BalanceReadOptions.SectionName, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

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
