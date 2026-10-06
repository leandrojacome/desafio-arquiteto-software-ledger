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
