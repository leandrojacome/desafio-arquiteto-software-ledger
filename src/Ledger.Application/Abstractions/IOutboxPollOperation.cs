namespace Ledger.Application.Abstractions;

public interface IOutboxPollOperation : IDisposable
{
    void BatchSize(int size);
}
