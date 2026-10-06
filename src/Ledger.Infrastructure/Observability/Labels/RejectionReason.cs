namespace Ledger.Infrastructure.Observability.Labels;

internal enum RejectionReason
{
    [Label("insufficient_funds")]
    InsufficientFunds = 0,

    [Label("currency_mismatch")]
    CurrencyMismatch = 1,

    [Label("account_not_found")]
    AccountNotFound = 2,

    [Label("idempotency_conflict")]
    IdempotencyConflict = 3,

    [Label("entry_already_reversed")]
    EntryAlreadyReversed = 4,

    [Label("entry_not_reversible")]
    EntryNotReversible = 5,

    [Label("entry_not_found")]
    EntryNotFound = 6,

    [Label("validation")]
    Validation = 7
}
