namespace Ledger.Application.Abstractions;

public interface IAccountCreationOperation : IDisposable
{
    void Outcome(string outcome);
}
