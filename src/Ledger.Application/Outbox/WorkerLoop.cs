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
