namespace Ledger.Application.Integrity;

public interface IIntegrityRunTelemetry : IDisposable
{
    void Violation(IntegrityCheck check);

    void AccountsChecked(long count);

    void Completed(bool clean);

    void Failed();
}
