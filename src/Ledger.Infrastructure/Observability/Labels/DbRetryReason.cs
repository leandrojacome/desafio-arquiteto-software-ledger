namespace Ledger.Infrastructure.Observability.Labels;

internal enum DbRetryReason
{
    [Label("deadlock")]
    Deadlock = 0,

    [Label("serialization_failure")]
    SerializationFailure = 1,

    [Label("transient_connection")]
    TransientConnection = 2
}
