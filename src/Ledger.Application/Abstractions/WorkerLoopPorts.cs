using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IWorkerHeartbeat
{
    void Beat(WorkerLoop loop);

    DateTimeOffset? LastBeat(WorkerLoop loop);
}

public interface IWorkerLoopTelemetry
{
    void CycleSucceeded(WorkerLoop loop);

    void CycleFailed(WorkerLoop loop);
}
