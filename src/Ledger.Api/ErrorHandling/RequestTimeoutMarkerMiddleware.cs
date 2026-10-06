using Microsoft.AspNetCore.Http.Timeouts;

namespace Ledger.Api.ErrorHandling;

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
