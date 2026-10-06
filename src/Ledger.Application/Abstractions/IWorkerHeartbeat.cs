using Ledger.Application.Outbox;

namespace Ledger.Application.Abstractions;

public interface IWorkerHeartbeat
{
    void Beat(WorkerLoop loop);

    DateTimeOffset? LastBeat(WorkerLoop loop);
}
