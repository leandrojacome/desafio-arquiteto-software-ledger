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

internal sealed class ConfigureRateLimiterOptions(RequestLimiters limiters, RateLimitRejectionWriter rejections)
    : IConfigureOptions<RateLimiterOptions>
{
    public void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = limiters.Chain;
        options.OnRejected = rejections.WriteAsync;
    }
}
