namespace Ledger.Application.Abstractions;

public interface IRewrapBatchOperation : IDisposable
{
    void Complete(int rewrapped, int failed);
}
