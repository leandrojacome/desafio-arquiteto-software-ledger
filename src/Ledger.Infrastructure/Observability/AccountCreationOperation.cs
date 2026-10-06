using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class AccountCreationOperation(Activity? activity) : IAccountCreationOperation
{
    private CreateAccountOutcome _outcome = CreateAccountOutcome.Failed;
    private bool _disposed;

    public void Outcome(string outcome)
    {
        if (LabelTable<CreateAccountOutcome>.TryParse(outcome, out var parsed))
        {
            _outcome = parsed;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (activity is null)
        {
            return;
        }

        activity.SetTag(SpanAttributes.Outcome, _outcome.Label());

        if (_outcome is CreateAccountOutcome.Failed or CreateAccountOutcome.KeyUnavailable)
        {
            activity.SetStatus(ActivityStatusCode.Error);
        }

        activity.Dispose();
    }
}
