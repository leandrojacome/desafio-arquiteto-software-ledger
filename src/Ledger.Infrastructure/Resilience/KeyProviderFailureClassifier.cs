using Ledger.Application.Abstractions;
using Ledger.Application.Security;

namespace Ledger.Infrastructure.Resilience;

internal sealed class KeyProviderFailureClassifier : ITransientFailureClassifier
{
    public bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is KeyProviderUnavailableException
               || (exception.InnerException is { } inner && IsTransient(inner));
    }
}
