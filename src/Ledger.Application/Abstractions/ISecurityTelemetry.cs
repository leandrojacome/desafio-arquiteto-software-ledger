namespace Ledger.Application.Abstractions;

public interface ISecurityTelemetry
{
    void AuthFailed(string reason);

    void RateLimitRejected(string policy);

    void AccountCreated();

    IAccountCreationOperation BeginCreateAccount();

    void PiiDecrypted(string purpose, int accounts);

    void KeyReloaded(bool succeeded);

    void AuditRecorded(string eventType, string outcome);

    void AuditSkipped(string reason);

    IRewrapBatchOperation BeginRewrapBatch();

    void AccountsRewrapped(int rewrapped, int failed);

    void KeyUsageObserved(long accountsBelowActive, bool anomalous);
}
