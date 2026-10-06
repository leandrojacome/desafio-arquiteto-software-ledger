using System.ComponentModel.DataAnnotations;

namespace Ledger.Api;

internal sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    [Range(0, 60)] public int OccurredAtFutureToleranceMinutes { get; init; } = 5;
}

internal static class LedgerOptionsServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerOptions(this IServiceCollection services)
    {
        services.AddOptions<LedgerOptions>()
            .BindConfiguration(LedgerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
