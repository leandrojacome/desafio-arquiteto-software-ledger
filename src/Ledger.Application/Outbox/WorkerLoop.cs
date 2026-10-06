namespace Ledger.Application.Outbox;

public enum WorkerLoop
{
    Outbox = 0,
    IntegrityRecent = 1,
    IntegrityFull = 2,
    Measure = 3,
    OutboxPrune = 4,
    IdempotencyPrune = 5,
    KeyRewrap = 6
}

public static class WorkerLoopExtensions
{
    public static string Name(this WorkerLoop loop)
    {
        return loop switch
        {
            WorkerLoop.Outbox => "outbox",
            WorkerLoop.IntegrityRecent => "integrity-recent",
            WorkerLoop.IntegrityFull => "integrity-full",
            WorkerLoop.Measure => "measure",
            WorkerLoop.OutboxPrune => "prune",
            WorkerLoop.IdempotencyPrune => "idempotency-prune",
            WorkerLoop.KeyRewrap => "rewrap",
            _ => throw new ArgumentOutOfRangeException(nameof(loop), loop, "Unknown worker loop.")
        };
    }
}
