using System.Globalization;
using System.Threading.RateLimiting;
using Ledger.Api.ErrorHandling;
using Ledger.Api.Security;
using Ledger.Application.Abstractions;
using Microsoft.AspNetCore.RateLimiting;

namespace Ledger.Api.RateLimiting;

internal sealed class RateLimitRejectionWriter(
    ISecurityTelemetry telemetry,
    ILogger<RateLimitRejectionWriter> logger)
{
    private const int MinimumRetryAfterSeconds = 1;

    public async ValueTask WriteAsync(OnRejectedContext rejection, CancellationToken _)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        var context = rejection.HttpContext;
        var policy = rejection.Lease.TryGetMetadata(NamedLease.PolicyMetadata, out var named) ? named : null;
        var clientId = CallerIdentity.ClientIdOf(context);
        var isConcurrency = policy is not null && RateLimitPolicyNames.IsConcurrency(policy);

        var status = isConcurrency
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status429TooManyRequests;
        var retryAfter = isConcurrency ? MinimumRetryAfterSeconds : RetryAfterOf(rejection.Lease);

        if (policy is not null)
        {
            telemetry.RateLimitRejected(policy);
            Log(policy, clientId, isConcurrency, retryAfter);
        }

        context.Response.StatusCode = status;
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        await context.RequestServices
            .GetRequiredService<IProblemDetailsService>()
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = ProblemFactory.Create(context, status, ProblemCatalog.CodeFor(status))
            });
    }

    private static int RetryAfterOf(RateLimitLease lease)
    {
        return lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? Math.Max(MinimumRetryAfterSeconds, (int)Math.Ceiling(delay.TotalSeconds))
            : MinimumRetryAfterSeconds;
    }

    private void Log(string policy, string? clientId, bool isConcurrency, int retryAfter)
    {
        if (isConcurrency)
        {
            RateLimitLog.ConcurrencyLimitExceeded(logger, policy, clientId);
        }
        else
        {
            RateLimitLog.RateLimitExceeded(logger, policy, clientId, retryAfter);
        }
    }
}
