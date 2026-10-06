namespace Ledger.Application.Integrity;

public interface IIntegrityTelemetry
{
    IIntegrityRunTelemetry BeginRun(IntegrityMode mode);
}

public interface IIntegrityRunTelemetry : IDisposable
{
    void Violation(IntegrityCheck check);

    void AccountsChecked(long count);

    void Completed(bool clean);

    void Failed();
}
