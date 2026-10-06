using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.RateLimiting;

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
