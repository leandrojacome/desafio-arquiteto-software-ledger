namespace Ledger.Application.Abstractions;

public interface IStatementOperation : IDisposable
{
    void Returned(int count, bool hasNext);
}
