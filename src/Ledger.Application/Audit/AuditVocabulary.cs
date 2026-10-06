using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Audit;

public static class AuditEventTypes
{
    public const string AccountCreated = "account.created";
    public const string AuthorizationDeniedWrite = "authorization.denied_write";
    public const string PiiDecrypted = "pii.decrypted";
    public const string PiiRewrapped = "pii.rewrapped";
    public const string KeysVersionActivated = "keys.version_activated";
    public const string IntegrityRunCompleted = "integrity.run_completed";
    public const string IntegrityViolationDetected = "integrity.violation_detected";
}

public static class AuditOutcome
{
    public const string Success = "SUCCESS";
    public const string Denied = "DENIED";
    public const string Failure = "FAILURE";
}

[SuppressMessage("Design", "CA1008",
    Justification = "A zero member would let an unset reason pass for a valid one; the default must stay outside the defined values.")]
public enum DeniedWriteReason
{
    InsufficientScope = 1,
    NotProvisioningClient = 2
}
