using OpenTelemetry.Metrics;

namespace Ledger.Infrastructure.Observability;

internal static class TelemetryViews
{
    public static MeterProviderBuilder AddLedgerViews(this MeterProviderBuilder builder)
    {
        foreach (var (instrument, boundaries) in HistogramBoundaries.ByInstrument)
        {
            builder.AddView(instrument, new ExplicitBucketHistogramConfiguration { Boundaries = boundaries });
        }

        return builder;
    }
}
