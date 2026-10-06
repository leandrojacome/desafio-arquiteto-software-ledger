using System.Globalization;
using Ledger.Api.Health;
using Ledger.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Ledger.Api.Endpoints;

internal static class HealthEndpoints
{
    private const string AllowedMethods = "GET, HEAD";

    private static readonly string[] RefusedMethods =
        [HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete, HttpMethods.Options];

    private static readonly HttpMethodMetadata ReadOnlyMethods = new([HttpMethods.Get, HttpMethods.Head]);

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var retryAfter = app.ServiceProvider
            .GetRequiredService<HealthRetryAfter>()
            .Seconds
            .ToString(CultureInfo.InvariantCulture);

        app.MapHealthChecks(ApiConstants.LiveRoute,
                new HealthCheckOptions
                {
                    Predicate = _ => false,
                    ResponseWriter = (context, report) => WriteAsync(context, report, retryAfter)
                })
            .AllowAnonymous()
            .DisableRequestTimeout()
            .ExcludeFromDescription()
            .WithMetadata(ReadOnlyMethods);

        app.MapHealthChecks(ApiConstants.ReadyRoute,
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
                    ResponseWriter = (context, report) => WriteAsync(context, report, retryAfter)
                })
            .AllowAnonymous()
            .DisableRequestTimeout()
            .ExcludeFromDescription()
            .WithMetadata(ReadOnlyMethods);

        MapRefusedMethods(app, ApiConstants.LiveRoute);
        MapRefusedMethods(app, ApiConstants.ReadyRoute);
    }

    private static void MapRefusedMethods(IEndpointRouteBuilder app, string route)
    {
        app.MapMethods(route, RefusedMethods, MethodNotAllowed)
            .AllowAnonymous()
            .DisableRequestTimeout()
            .ExcludeFromDescription();
    }

    private static IResult MethodNotAllowed(HttpContext context)
    {
        context.Response.Headers.Allow = AllowedMethods;

        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private static Task WriteAsync(HttpContext context, HealthReport report, string retryAfter)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (report.Status == HealthStatus.Unhealthy)
        {
            context.Response.Headers.RetryAfter = retryAfter;
        }

        return context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()), context.RequestAborted);
    }
}
