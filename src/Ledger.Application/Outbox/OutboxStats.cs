namespace Ledger.Application.Outbox;

public sealed record OutboxStats(long Pending, double? OldestPendingAgeSeconds, long Failed);

public sealed record OutboxStatsRequest(int PendingCap, int FailedAttempts, int FailedHeadWindow);
