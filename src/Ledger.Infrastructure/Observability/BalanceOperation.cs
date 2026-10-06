using System.Diagnostics;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class BalanceOperation(
    LedgerMeters meters,
    TimeProvider timeProvider,
    Activity? activity,
    BalanceMode? mode) : IDisposable
{
    private readonly long _startedAt = timeProvider.GetTimestamp();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (mode is { } label)
        {
            var elapsed = timeProvider.GetElapsedTime(_startedAt);

            meters.BalanceQueryDuration.Record(elapsed.TotalSeconds, new TagList { { TagKeys.Mode, label.Label() } });
        }

        activity?.Dispose();
    }
}
