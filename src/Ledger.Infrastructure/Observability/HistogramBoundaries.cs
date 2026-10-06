using System.Collections.Frozen;

namespace Ledger.Infrastructure.Observability;

internal static class HistogramBoundaries
{
    public static readonly double[] HttpRequest = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5, 5];

    public static readonly double[] UseCase = [0.002, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.15, 0.25, 0.5, 1, 2.5];

    public static readonly double[] DbCommand = [0.001, 0.002, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1];

    public static readonly double[] OutboxPublish = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5];

    public static readonly double[] IntegrityRun = [0.1, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 900, 1800, 3600];

    public static readonly FrozenDictionary<string, double[]> ByInstrument = new Dictionary<string, double[]>
    {
        [MetricNames.HttpServerRequestDuration] = HttpRequest,
        [MetricNames.EntryDuration] = UseCase,
        [MetricNames.BalanceQueryDuration] = UseCase,
        [MetricNames.DbCommandDuration] = DbCommand,
        [MetricNames.OutboxPublishDuration] = OutboxPublish,
        [MetricNames.IntegrityDuration] = IntegrityRun
    }.ToFrozenDictionary();
}
