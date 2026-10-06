using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class StubWorkerHeartbeat : IWorkerHeartbeat
{
    private readonly ConcurrentDictionary<WorkerLoop, DateTimeOffset> _beats = new();

    public void Beat(WorkerLoop loop) => _beats[loop] = DateTimeOffset.UnixEpoch;

    public void Set(WorkerLoop loop, DateTimeOffset at) => _beats[loop] = at;

    public DateTimeOffset? LastBeat(WorkerLoop loop) => _beats.TryGetValue(loop, out var at) ? at : null;
}
