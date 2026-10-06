namespace Ledger.Application.Idempotency;

public sealed record IdempotencyPruneSettings(TimeSpan Retention, int BatchSize, TimeSpan Interval);
