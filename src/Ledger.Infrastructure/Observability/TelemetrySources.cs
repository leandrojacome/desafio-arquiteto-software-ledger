using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ledger.Infrastructure.Observability;

internal sealed record TelemetrySources(Meter Meter, ActivitySource Source)
{
    public static TelemetrySources Default { get; } = new(Telemetry.Meter, Telemetry.Source);
}

internal static class Telemetry
{
    public const string Name = "Ledger";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);
}
