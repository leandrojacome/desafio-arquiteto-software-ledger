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
