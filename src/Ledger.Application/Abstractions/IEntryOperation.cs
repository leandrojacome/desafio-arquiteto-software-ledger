namespace Ledger.Application.Abstractions;

public interface IEntryOperation : IDisposable
{
    void Recorded(string type);

    void Replayed();

    void Rejected(string reason);

    void IdempotencyConflict();

    void RecordedAtCorrected();
}
