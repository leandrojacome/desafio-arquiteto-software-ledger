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
