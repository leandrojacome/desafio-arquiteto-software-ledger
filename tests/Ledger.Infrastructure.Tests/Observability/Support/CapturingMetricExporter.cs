using System.Collections.Concurrent;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class CapturingMetricExporter : BaseExporter<Metric>
{
    private readonly ConcurrentQueue<ExportedMetric> _metrics = new();

    public IReadOnlyList<ExportedMetric> Metrics => [.. _metrics];

    public override ExportResult Export(in Batch<Metric> batch)
    {
        foreach (var metric in batch)
        {
            var bounds = new List<double>();

            if (metric.MetricType == MetricType.Histogram)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    foreach (var bucket in point.GetHistogramBuckets())
                    {
                        if (!double.IsPositiveInfinity(bucket.ExplicitBound))
                        {
                            bounds.Add(bucket.ExplicitBound);
                        }
                    }

                    break;
                }
            }

            _metrics.Enqueue(new ExportedMetric(metric.Name, metric.Unit, bounds));
        }

        return ExportResult.Success;
    }
}
