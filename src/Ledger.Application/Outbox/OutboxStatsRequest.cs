namespace Ledger.Application.Outbox;

public sealed record OutboxStatsRequest(int PendingCap, int FailedAttempts, int FailedHeadWindow);
