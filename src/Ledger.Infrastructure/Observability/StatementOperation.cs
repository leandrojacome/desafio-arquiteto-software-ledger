using System.Diagnostics;
using Ledger.Application.Abstractions;

namespace Ledger.Infrastructure.Observability;

internal sealed class StatementOperation(Activity? activity) : IStatementOperation
{
    public void Returned(int count, bool hasNext)
    {
        activity?.SetTag(SpanAttributes.StatementReturned, count);
        activity?.SetTag(SpanAttributes.StatementHasNext, hasNext);
    }

    public void Dispose()
    {
        activity?.Dispose();
    }
}
