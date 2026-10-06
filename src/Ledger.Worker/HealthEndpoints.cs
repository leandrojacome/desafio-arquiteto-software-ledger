using System.Globalization;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Resilience;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Worker;

internal static class HealthEndpoints
{
    private static readonly string[] AllowedMethods = ["GET", "HEAD"];

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var retryAfter = app.ServiceProvider
            .GetRequiredService<IOptions<ResilienceOptions>>()
            .Value.Health.RetryAfterSeconds
            .ToString(CultureInfo.InvariantCulture);

        app.MapHealthChecks(
                WorkerConstants.LiveRoute,
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains(WorkerHealthTags.Live),
                    ResponseWriter = (context, report) => WriteAsync(context, report, retryAfter)
                })
            .WithMetadata(new HttpMethodMetadata(AllowedMethods));

        app.MapHealthChecks(
                WorkerConstants.ReadyRoute,
                new HealthCheckOptions
                {
                    Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
                    ResponseWriter = (context, report) => WriteAsync(context, report, retryAfter)
                })
            .WithMetadata(new HttpMethodMetadata(AllowedMethods));
    }

    private static Task WriteAsync(HttpContext context, HealthReport report, string retryAfter)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (report.Status == HealthStatus.Unhealthy)
        {
            context.Response.Headers.RetryAfter = retryAfter;
        }

        return HttpMethods.IsHead(context.Request.Method)
            ? Task.CompletedTask
            : context.Response.WriteAsJsonAsync(new HealthResponse(report.Status.ToString()), context.RequestAborted);
    }
}
