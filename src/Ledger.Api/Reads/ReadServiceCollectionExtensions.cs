using Ledger.Api.Validation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Reads;

internal static class ReadServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerReads(this IServiceCollection services)
    {
        services.AddOptions<StatementOptions>()
            .BindConfiguration(StatementOptions.SectionName)
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StatementOptions>, StatementOptionsValidator>());
        services.TryAddSingleton<StatementQueryReader>();

        return services;
    }
}
