namespace Ledger.Application.Abstractions;

public interface ITransientFailureClassifier
{
    bool IsTransient(Exception exception);
}
