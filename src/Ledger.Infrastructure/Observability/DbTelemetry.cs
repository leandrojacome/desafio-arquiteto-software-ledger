using System.Diagnostics;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class DbTelemetry(LedgerMeters meters)
{
    public void CommandCompleted(DbOperation operation, TimeSpan elapsed)
    {
        meters.DbCommandDuration.Record(elapsed.TotalSeconds, new TagList { { TagKeys.Operation, operation.Label() } });
    }

    public void TransactionRetried(DbRetryReason reason)
    {
        meters.DbRetries.Add(1, new TagList { { TagKeys.Reason, reason.Label() } });
    }
}
