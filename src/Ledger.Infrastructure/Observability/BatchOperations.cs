using System.Diagnostics;
using Ledger.Application.Abstractions;

namespace Ledger.Infrastructure.Observability;

internal sealed class OutboxPollOperation(Activity? activity) : IOutboxPollOperation
{
    public void BatchSize(int size)
    {
        activity?.SetTag(SpanAttributes.OutboxBatchSize, size);
    }

    public void Dispose()
    {
        activity?.Dispose();
    }
}

internal sealed class RewrapBatchOperation(Activity? activity) : IRewrapBatchOperation
{
    public void Complete(int rewrapped, int failed)
    {
        activity?.SetTag(SpanAttributes.RewrapAccounts, rewrapped);
        activity?.SetTag(SpanAttributes.RewrapFailed, failed);
    }

    public void Dispose()
    {
        activity?.Dispose();
    }
}
