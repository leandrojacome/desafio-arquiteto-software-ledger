namespace Ledger.Infrastructure.Observability;

internal static class SpanAttributes
{
    public const string EntryType = "ledger.entry.type";
    public const string Outcome = "ledger.outcome";
    public const string IdempotentReplay = "ledger.idempotent_replay";
    public const string BalanceMode = "ledger.balance.mode";
    public const string StatementLimit = "ledger.statement.limit";
    public const string StatementReturned = "ledger.statement.returned";
    public const string StatementHasNext = "ledger.statement.has_next";
    public const string OutboxBatchSize = "outbox.batch_size";
    public const string OutboxAttempt = "ledger.outbox.attempt";
    public const string MessagingSystem = "messaging.system";
    public const string MessagingDestination = "messaging.destination.name";
    public const string RewrapAccounts = "ledger.rewrap.accounts";
    public const string RewrapFailed = "ledger.rewrap.failed";
    public const string IntegrityMode = "integrity.mode";
    public const string IntegrityAccountsChecked = "integrity.accounts_checked";
}
