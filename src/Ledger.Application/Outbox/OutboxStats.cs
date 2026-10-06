namespace Ledger.Application.Outbox;

public sealed record OutboxStats(long Pending, double? OldestPendingAgeSeconds, long Failed);
