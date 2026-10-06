using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ledger.Infrastructure.Observability;

internal sealed record TelemetrySources(Meter Meter, ActivitySource Source)
{
    public static TelemetrySources Default { get; } = new(Telemetry.Meter, Telemetry.Source);
}
