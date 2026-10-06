using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry;

namespace Ledger.Api.IntegrationTests.Observability;

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
