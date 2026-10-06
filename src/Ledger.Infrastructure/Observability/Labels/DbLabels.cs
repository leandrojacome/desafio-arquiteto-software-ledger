namespace Ledger.Infrastructure.Observability.Labels;

internal enum DbOperation
{
    [Label("insert_entry")]
    InsertEntry = 0,

    [Label("update_balance")]
    UpdateBalance = 1,

    [Label("insert_outbox")]
    InsertOutbox = 2,

    [Label("insert_idempotency_key")]
    InsertIdempotencyKey = 3,

    [Label("select_balance")]
    SelectBalance = 4,

    [Label("select_balance_as_of")]
    SelectBalanceAsOf = 5,

    [Label("select_entries")]
    SelectEntries = 6
}

internal enum DbRetryReason
{
    [Label("deadlock")]
    Deadlock = 0,

    [Label("serialization_failure")]
    SerializationFailure = 1,

    [Label("transient_connection")]
    TransientConnection = 2
}
