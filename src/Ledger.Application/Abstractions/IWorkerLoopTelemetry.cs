using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IWorkerLoopTelemetry
{
    void CycleSucceeded(WorkerLoop loop);

    void CycleFailed(WorkerLoop loop);
}
