using System.Collections.Concurrent;
using Ledger.Application.Integrity;

namespace Ledger.Infrastructure.Observability;

internal sealed class IntegritySuccessTracker
{
    private readonly ConcurrentDictionary<IntegrityMode, long> _lastSuccessMilliseconds = new();

    public IReadOnlyList<KeyValuePair<IntegrityMode, double>> Snapshot()
    {
        return
        [
            .. _lastSuccessMilliseconds.Select(pair =>
                new KeyValuePair<IntegrityMode, double>(pair.Key, pair.Value / 1000d))
        ];
    }

    public void Record(IntegrityMode mode, DateTimeOffset at)
    {
        _lastSuccessMilliseconds[mode] = at.ToUnixTimeMilliseconds();
    }
}
