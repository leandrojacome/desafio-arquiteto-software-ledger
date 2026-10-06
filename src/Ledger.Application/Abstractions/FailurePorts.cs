namespace Ledger.Application.Abstractions;

public interface IDependencyFailureInspector
{
    string? SqlState(Exception exception);
}

public interface ITransientFailureClassifier
{
    bool IsTransient(Exception exception);
}
