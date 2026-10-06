namespace Ledger.Application.Abstractions;

public interface IDependencyFailureInspector
{
    string? SqlState(Exception exception);
}
