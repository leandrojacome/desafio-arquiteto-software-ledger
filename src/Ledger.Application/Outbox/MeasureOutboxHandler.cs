using Ledger.Application.Abstractions;

namespace Ledger.Application.Outbox;

public sealed class MeasureOutboxHandler(IOutboxQueue queue, IOutboxTelemetry telemetry, OutboxSettings settings)
{
    public async Task<OutboxStats> HandleAsync(CancellationToken cancellationToken)
    {
        var request = new OutboxStatsRequest(settings.PendingCap, settings.FailedAttempts, settings.FailedHeadWindow);
        var stats = await queue.ReadStatsAsync(request, cancellationToken);
        var measured = stats with { OldestPendingAgeSeconds = stats.OldestPendingAgeSeconds ?? 0d };

        telemetry.Measured(measured);

        return measured;
    }
}
