using System.Collections.Concurrent;
using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Observability;

internal sealed class WorkerLoopSuccesses
{
    private readonly ConcurrentDictionary<WorkerLoop, long> _lastSuccessMilliseconds = new();

    public IReadOnlyList<KeyValuePair<WorkerLoop, double>> Snapshot()
    {
        return
        [
            .. _lastSuccessMilliseconds.Select(pair =>
                new KeyValuePair<WorkerLoop, double>(pair.Key, pair.Value / 1000d))
        ];
    }

    public void Record(WorkerLoop loop, DateTimeOffset at)
    {
        _lastSuccessMilliseconds[loop] = at.ToUnixTimeMilliseconds();
    }
}
