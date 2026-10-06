namespace Ledger.Application.Abstractions;

public interface IReadTelemetry
{
    IDisposable BeginBalance(string mode);

    IStatementOperation BeginStatement(int limit);
}
