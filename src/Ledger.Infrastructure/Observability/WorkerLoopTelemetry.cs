using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class WorkerLoopTelemetry(
    LedgerMeters meters,
    WorkerLoopSuccesses successes,
    TimeProvider timeProvider) : IWorkerLoopTelemetry
{
    public void CycleSucceeded(WorkerLoop loop)
    {
        successes.Record(loop, timeProvider.GetUtcNow());
    }

    public void CycleFailed(WorkerLoop loop)
    {
        meters.WorkerLoopFailures.Add(1, new TagList { { TagKeys.Loop, loop.Label() } });
    }
}
