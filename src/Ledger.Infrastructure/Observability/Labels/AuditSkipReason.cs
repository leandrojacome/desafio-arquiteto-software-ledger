namespace Ledger.Infrastructure.Observability.Labels;

internal enum AuditSkipReason
{
    [Label("rate_capped")]
    RateCapped = 0,

    [Label("write_failed")]
    WriteFailed = 1
}
