using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.RateLimiting;

internal static class RateLimitingServiceCollectionExtensions
{
    public static void AddLedgerRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();
        services.AddSingleton<RequestLimiters>();
        services.AddSingleton<RateLimitRejectionWriter>();
        services.AddRateLimiter(_ => { });
        services.AddSingleton<IConfigureOptions<RateLimiterOptions>, ConfigureRateLimiterOptions>();
    }
}
