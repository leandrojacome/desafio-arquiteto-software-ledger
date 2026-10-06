using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;

namespace Ledger.Infrastructure.Resilience;

internal sealed class CompositeTransientFailureClassifier(
    PostgresTransientFailureClassifier database,
    KeyProviderFailureClassifier keys) : ITransientFailureClassifier
{
    public bool IsTransient(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return database.IsTransient(exception) || keys.IsTransient(exception);
    }
}
