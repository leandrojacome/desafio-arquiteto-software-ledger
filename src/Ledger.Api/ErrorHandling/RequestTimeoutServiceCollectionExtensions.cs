using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;

namespace Ledger.Api.ErrorHandling;

internal static class RequestTimeoutServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerRequestTimeouts(this IServiceCollection services)
    {
        services.AddRequestTimeouts(_ => { });
        services.AddSingleton<IConfigureOptions<RequestTimeoutOptions>, ConfigureRequestTimeouts>();

        return services;
    }
}
