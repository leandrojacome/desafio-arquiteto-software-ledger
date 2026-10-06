namespace Ledger.Infrastructure.Observability;

internal static class MetricNames
{
    public const string EntriesRecorded = "ledger.entries.recorded";
    public const string EntriesRejected = "ledger.entries.rejected";
    public const string EntryDuration = "ledger.entry.duration";
    public const string IdempotencyReplays = "idempotency.replays";
    public const string IdempotencyConflicts = "idempotency.conflicts";
    public const string RecordedAtCorrections = "ledger.recorded_at.corrections";
    public const string DbRetries = "ledger.db.retries";
    public const string DbCommandDuration = "ledger.db.command.duration";
    public const string BalanceQueryDuration = "ledger.balance.query.duration";

    public const string AuthFailures = "ledger.auth.failures";
    public const string RateLimitRejections = "ledger.rate_limit.rejections";
    public const string AccountsCreated = "ledger.accounts.created";
    public const string PiiDecrypt = "ledger.pii.decrypt";
    public const string KeyReloads = "ledger.key.reloads";
    public const string AuditRecorded = "ledger.audit.recorded";
    public const string AuditSkipped = "ledger.audit.skipped";
    public const string RewrapAccounts = "ledger.rewrap.accounts";
    public const string PiiAccountsBelowActiveKey = "ledger.pii.accounts.below_active_key";

    public const string OutboxPending = "outbox.pending.messages";
    public const string OutboxOldestPendingAge = "outbox.oldest_pending.age";
    public const string OutboxPublished = "outbox.published";
    public const string OutboxPublishFailures = "outbox.publish.failures";
    public const string OutboxPublishDuration = "outbox.publish.duration";
    public const string OutboxFailed = "outbox.failed.messages";
    public const string OutboxPruned = "outbox.pruned";
    public const string BrokerCircuitState = "broker.circuit_breaker.state";
    public const string BrokerConnected = "broker.connected";
    public const string WorkerLastCycle = "worker.last_cycle.timestamp";
    public const string WorkerLoopFailures = "ledger.worker.loop.failures";
    public const string WorkerLoopLastSuccess = "ledger.worker.loop.last_success.timestamp";

    public const string IntegrityRuns = "ledger.integrity.check.runs";
    public const string IntegrityViolations = "ledger.integrity.violations";
    public const string IntegrityLastSuccess = "ledger.integrity.last.success.timestamp";
    public const string IntegrityDuration = "ledger.integrity.check.duration";

    public const string HttpServerRequestDuration = "http.server.request.duration";
}
