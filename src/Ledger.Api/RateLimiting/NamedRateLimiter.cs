using System.Diagnostics.CodeAnalysis;
using System.Threading.RateLimiting;

namespace Ledger.Api.RateLimiting;

internal sealed class NamedRateLimiter(
    PartitionedRateLimiter<HttpContext> inner,
    Func<HttpContext, string> policyFor) : PartitionedRateLimiter<HttpContext>
{
    public override RateLimiterStatistics? GetStatistics(HttpContext resource) => inner.GetStatistics(resource);

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The failed lease is handed to the caller, which disposes it.")]
    protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount)
    {
        var lease = inner.AttemptAcquire(resource, permitCount);

        return lease.IsAcquired ? lease : new NamedLease(lease, policyFor(resource));
    }

    [SuppressMessage("Reliability", "CA2000",
        Justification = "The failed lease is handed to the caller, which disposes it.")]
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resource,
        int permitCount,
        CancellationToken cancellationToken)
    {
        var lease = await inner.AcquireAsync(resource, permitCount, cancellationToken);

        return lease.IsAcquired ? lease : new NamedLease(lease, policyFor(resource));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
