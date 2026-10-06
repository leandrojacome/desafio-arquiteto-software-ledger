using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;

namespace Ledger.Infrastructure.Health;

internal sealed class WorkerHeartbeat : IWorkerHeartbeat
{
    private static readonly int LoopCount = Enum.GetValues<WorkerLoop>().Length;

    private readonly TimeProvider _timeProvider;
    private readonly long[] _beats = new long[LoopCount];

    public WorkerHeartbeat(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        var startedAt = timeProvider.GetUtcNow().UtcTicks;

        foreach (var loop in Enum.GetValues<WorkerLoop>())
        {
            Interlocked.Exchange(ref _beats[(int)loop], startedAt);
        }
    }

    public void Beat(WorkerLoop loop)
    {
        Interlocked.Exchange(ref _beats[SlotOf(loop)], _timeProvider.GetUtcNow().UtcTicks);
    }

    public DateTimeOffset? LastBeat(WorkerLoop loop)
    {
        return new DateTimeOffset(Interlocked.Read(ref _beats[SlotOf(loop)]), TimeSpan.Zero);
    }

    private static int SlotOf(WorkerLoop loop)
    {
        var slot = (int)loop;

        return slot >= 0 && slot < LoopCount
            ? slot
            : throw new ArgumentOutOfRangeException(nameof(loop), loop, "Unknown worker loop.");
    }
}
