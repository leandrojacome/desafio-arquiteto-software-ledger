namespace Ledger.Api;

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
