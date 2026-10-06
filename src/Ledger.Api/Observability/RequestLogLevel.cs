using Serilog.Events;

namespace Ledger.Api.Observability;

internal static class RequestLogLevel
{
    private const double SlowRequestThresholdMilliseconds = 150;

    public static LogEventLevel For(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null)
        {
            return LogEventLevel.Error;
        }

        var status = context.Response.StatusCode;
        var isHealth = context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);

        if (isHealth && status == StatusCodes.Status503ServiceUnavailable)
        {
            return LogEventLevel.Verbose;
        }

        if (status >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (isHealth)
        {
            return LogEventLevel.Verbose;
        }

        if (status >= StatusCodes.Status400BadRequest)
        {
            return LogEventLevel.Information;
        }

        return elapsedMilliseconds > SlowRequestThresholdMilliseconds ? LogEventLevel.Warning : LogEventLevel.Debug;
    }
}
