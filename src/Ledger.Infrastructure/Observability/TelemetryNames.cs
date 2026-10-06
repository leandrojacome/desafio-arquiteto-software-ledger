namespace Ledger.Infrastructure.Observability;

internal static class SpanNames
{
    public const string RecordEntry = "ledger.record_entry";
    public const string BalanceQuery = "ledger.balance_query";
    public const string StatementQuery = "ledger.statement_query";
    public const string CreateAccount = "ledger.create_account";
    public const string OutboxPoll = "outbox.poll";
    public const string OutboxPublish = "outbox.publish";
    public const string RewrapBatch = "pii.rewrap";
    public const string IntegrityCheck = "integrity.check";
}

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

internal static class TagKeys
{
    public const string Type = "type";
    public const string Client = "client";
    public const string Reason = "reason";
    public const string Outcome = "outcome";
    public const string Mode = "mode";
    public const string Operation = "operation";
    public const string Policy = "policy";
    public const string Purpose = "purpose";
    public const string Result = "result";
    public const string EventType = "event_type";
    public const string Kind = "kind";
    public const string Loop = "loop";
}
