using System.Globalization;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Options;

namespace Ledger.Api.ErrorHandling;

internal sealed class ConfigureRequestTimeouts(IOptions<ResilienceOptions> resilience)
    : IConfigureOptions<RequestTimeoutOptions>
{
    public void Configure(RequestTimeoutOptions options)
    {
        var settings = resilience.Value;
        var retryAfter = settings.ServiceUnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
            TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
            WriteTimeoutResponse = context => WriteAsync(context, retryAfter)
        };
    }

    private static async Task WriteAsync(HttpContext context, string retryAfter)
    {
        const int status = StatusCodes.Status503ServiceUnavailable;

        context.Response.Headers.RetryAfter = retryAfter;

        await context.RequestServices
            .GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = ProblemFactory.Create(context, status, ProblemCatalog.CodeFor(status))
            });
    }
}
