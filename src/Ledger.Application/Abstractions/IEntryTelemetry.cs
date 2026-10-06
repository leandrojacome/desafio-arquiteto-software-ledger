namespace Ledger.Application.Abstractions;

public interface IEntryTelemetry
{
    IEntryOperation Begin(string operation);
}

public interface IEntryOperation : IDisposable
{
    void Recorded(string type);

    void Replayed();

    void Rejected(string reason);

    void IdempotencyConflict();

    void RecordedAtCorrected();
}
