using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class CapturingActivityExporter : BaseExporter<Activity>
{
    private readonly ConcurrentQueue<string> _names = new();

    public IReadOnlyList<string> Names => [.. _names];

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            _names.Enqueue(activity.OperationName);
        }

        return ExportResult.Success;
    }
}

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

internal sealed record ExportedMetric(string Name, string? Unit, IReadOnlyList<double> Boundaries);
