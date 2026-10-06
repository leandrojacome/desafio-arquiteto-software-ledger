namespace Ledger.Application.Abstractions;

public interface IReadTelemetry
{
    IDisposable BeginBalance(string mode);

    IStatementOperation BeginStatement(int limit);
}

public interface IStatementOperation : IDisposable
{
    void Returned(int count, bool hasNext);
}
