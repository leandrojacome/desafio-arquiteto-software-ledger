using Ledger.Api.Validation;

namespace Ledger.Api.Endpoints;

internal static class WriteServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerWrites(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLedgerOptions();
        services.AddSingleton<RegisterEntryRequestReader>();

        return services;
    }
}
