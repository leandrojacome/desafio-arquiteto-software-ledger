using System.Diagnostics.CodeAnalysis;

namespace Ledger.Infrastructure.Security;

[SuppressMessage("Design", "CA1032",
    Justification = "The problem and the message are required, so the standard parameterless constructors would build an invalid exception.")]
internal sealed class KeyMaterialRejectedException : InvalidOperationException
{
    public KeyMaterialRejectedException(KeyMaterialProblem problem, string message)
        : base(message)
    {
        Problem = problem;
    }

    public KeyMaterialProblem Problem { get; }
}
