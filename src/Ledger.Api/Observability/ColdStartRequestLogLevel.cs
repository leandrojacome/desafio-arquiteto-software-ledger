using System.Collections.Concurrent;
using Serilog.Events;

namespace Ledger.Api.Observability;

internal sealed class ColdStartRequestLogLevel
{
    private const string UnmatchedRoute = "unmatched";

    private readonly ConcurrentDictionary<string, byte> _warmedRoutes = new(StringComparer.Ordinal);

    public LogEventLevel For(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        var level = RequestLogLevel.For(context, elapsedMilliseconds, exception);

        if (level is LogEventLevel.Verbose or LogEventLevel.Error || context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return level;
        }

        if (!_warmedRoutes.TryAdd(RouteOf(context), 0))
        {
            return level;
        }

        return level == LogEventLevel.Warning ? LogEventLevel.Information : level;
    }

    private static string RouteOf(HttpContext context)
    {
        return context.GetEndpoint()?.DisplayName ?? UnmatchedRoute;
    }
}
