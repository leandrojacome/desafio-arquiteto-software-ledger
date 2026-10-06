namespace Ledger.Application.Integrity;

public interface IIntegrityTelemetry
{
    IIntegrityRunTelemetry BeginRun(IntegrityMode mode);
}
