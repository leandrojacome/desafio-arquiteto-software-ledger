using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ledger.Infrastructure.Observability;

internal static class Telemetry
{
    public const string Name = "Ledger";

    public static readonly ActivitySource Source = new(Name);

    public static readonly Meter Meter = new(Name);
}
