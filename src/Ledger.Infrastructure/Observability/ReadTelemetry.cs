using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class ReadTelemetry(TelemetrySources sources, LedgerMeters meters, TimeProvider timeProvider)
    : IReadTelemetry
{
    public IDisposable BeginBalance(string mode)
    {
        BalanceMode? parsed = LabelTable<BalanceMode>.TryParse(mode, out var value) ? value : null;
        var activity = sources.Source.StartActivity(SpanNames.BalanceQuery, ActivityKind.Internal);

        if (parsed is { } label)
        {
            activity?.SetTag(SpanAttributes.BalanceMode, label.Label());
        }

        return new BalanceOperation(meters, timeProvider, activity, parsed);
    }

    public IStatementOperation BeginStatement(int limit)
    {
        var activity = sources.Source.StartActivity(SpanNames.StatementQuery, ActivityKind.Internal);
        activity?.SetTag(SpanAttributes.StatementLimit, limit);

        return new StatementOperation(activity);
    }
}

internal sealed class BalanceOperation(
    LedgerMeters meters,
    TimeProvider timeProvider,
    Activity? activity,
    BalanceMode? mode) : IDisposable
{
    private readonly long _startedAt = timeProvider.GetTimestamp();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (mode is { } label)
        {
            var elapsed = timeProvider.GetElapsedTime(_startedAt);

            meters.BalanceQueryDuration.Record(elapsed.TotalSeconds, new TagList { { TagKeys.Mode, label.Label() } });
        }

        activity?.Dispose();
    }
}

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
