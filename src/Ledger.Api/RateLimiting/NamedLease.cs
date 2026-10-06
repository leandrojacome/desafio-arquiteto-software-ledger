using System.Threading.RateLimiting;

namespace Ledger.Api.RateLimiting;

internal sealed class NamedLease(RateLimitLease inner, string policy) : RateLimitLease
{
    public const string PolicyMetadataName = "LEDGER_POLICY";

    public static MetadataName<string> PolicyMetadata { get; } = MetadataName.Create<string>(PolicyMetadataName);

    public override bool IsAcquired => false;

    public override IEnumerable<string> MetadataNames => inner.MetadataNames.Append(PolicyMetadataName);

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (metadataName == PolicyMetadataName)
        {
            metadata = policy;

            return true;
        }

        return inner.TryGetMetadata(metadataName, out metadata);
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
