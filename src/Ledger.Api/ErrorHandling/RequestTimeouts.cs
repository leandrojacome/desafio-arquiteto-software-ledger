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

internal static class RequestTimeoutServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerRequestTimeouts(this IServiceCollection services)
    {
        services.AddRequestTimeouts(_ => { });
        services.AddSingleton<IConfigureOptions<RequestTimeoutOptions>, ConfigureRequestTimeouts>();

        return services;
    }
}

internal sealed class RequestTimeoutMarkerMiddleware(RequestDelegate next)
{
    private const string TimedOutItem = "Ledger.RequestTimedOut";

    public static bool TimedOut(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Items.ContainsKey(TimedOutItem)
               || context.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await next(context);
        }
        catch (Exception) when (Mark(context))
        {
            throw;
        }
    }

    private static bool Mark(HttpContext context)
    {
        if (context.Features.Get<IHttpRequestTimeoutFeature>()?.RequestTimeoutToken.IsCancellationRequested == true)
        {
            context.Items[TimedOutItem] = true;
        }

        return false;
    }
}
