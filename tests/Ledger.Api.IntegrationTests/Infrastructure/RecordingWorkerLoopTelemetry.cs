using System.Collections.Concurrent;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class RecordingWorkerLoopTelemetry : IWorkerLoopTelemetry
{
    private readonly ConcurrentQueue<WorkerLoop> _succeeded = new();
    private readonly ConcurrentQueue<WorkerLoop> _failed = new();

    public IReadOnlyCollection<WorkerLoop> Succeeded => _succeeded;

    public IReadOnlyCollection<WorkerLoop> Failed => _failed;

    public void CycleSucceeded(WorkerLoop loop) => _succeeded.Enqueue(loop);

    public void CycleFailed(WorkerLoop loop) => _failed.Enqueue(loop);
}
