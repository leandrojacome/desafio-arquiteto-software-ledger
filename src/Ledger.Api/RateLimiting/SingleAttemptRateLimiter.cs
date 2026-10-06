using System.Threading.RateLimiting;

namespace Ledger.Api.RateLimiting;

internal sealed class SingleAttemptRateLimiter(PartitionedRateLimiter<HttpContext> inner)
    : PartitionedRateLimiter<HttpContext>
{
    private const string RefusalItem = "Ledger.RateLimitRefusal";

    public override RateLimiterStatistics? GetStatistics(HttpContext resource) => inner.GetStatistics(resource);

    protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount)
    {
        var lease = inner.AttemptAcquire(resource, permitCount);

        if (!lease.IsAcquired)
        {
            resource.Items[RefusalItem] = lease;
        }

        return lease;
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(
        HttpContext resource,
        int permitCount,
        CancellationToken cancellationToken)
    {
        if (resource.Items.TryGetValue(RefusalItem, out var refused) && refused is RateLimitLease lease)
        {
            return new ValueTask<RateLimitLease>(lease);
        }

        return inner.AcquireAsync(resource, permitCount, cancellationToken);
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
