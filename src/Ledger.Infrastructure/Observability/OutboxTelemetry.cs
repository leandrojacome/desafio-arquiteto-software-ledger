using System.Diagnostics;
using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Ledger.Infrastructure.Observability.Labels;

namespace Ledger.Infrastructure.Observability;

internal sealed class OutboxTelemetry(
    TelemetrySources sources,
    LedgerMeters meters,
    OutboxStatsHolder holder,
    TimeProvider timeProvider) : IOutboxTelemetry
{
    public const string MessagingSystem = "rabbitmq";
    public const string MessagingDestination = "ledger.events";

    public IOutboxPollOperation BeginPoll()
    {
        holder.MarkWorker();

        var activity = sources.Source.StartActivity(SpanNames.OutboxPoll, ActivityKind.Internal);

        return new OutboxPollOperation(activity);
    }

    public IOutboxPublishOperation BeginPublish(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        holder.MarkWorker();

        var previous = Activity.Current;
        Activity.Current = null;

        var activity = sources.Source.StartActivity(
            SpanNames.OutboxPublish,
            ActivityKind.Producer,
            default(ActivityContext),
            PublishTags(envelope),
            LinkTo(envelope));

        if (activity is null)
        {
            Activity.Current = previous;
        }

        return new OutboxPublishOperation(meters, timeProvider, activity, previous);
    }

    public void Published(int count)
    {
        if (count > 0)
        {
            meters.OutboxPublished.Add(count);
        }
    }

    public void PublishFailed(PublishFailureReason reason)
    {
        meters.OutboxPublishFailures.Add(1, new TagList { { TagKeys.Reason, reason.ToLabel() } });
    }

    public void Pruned(int count)
    {
        if (count > 0)
        {
            meters.OutboxPruned.Add(count);
        }
    }

    public void Measured(OutboxStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        holder.RecordStats(stats);
    }

    public void CircuitStateChanged(BrokerCircuitState state)
    {
        holder.RecordCircuit(state);
    }

    public void BrokerConnected(bool connected)
    {
        holder.RecordConnected(connected);
    }

    private static List<KeyValuePair<string, object?>> PublishTags(OutboxEnvelope envelope)
    {
        return
        [
            new(SpanAttributes.MessagingSystem, MessagingSystem),
            new(SpanAttributes.MessagingDestination, MessagingDestination),
            new(SpanAttributes.OutboxAttempt, envelope.Attempts),
            new(ActivityTags.CorrelationId, envelope.CorrelationId)
        ];
    }

    private static List<ActivityLink> LinkTo(OutboxEnvelope envelope)
    {
        if (envelope.TraceParent is { Length: > 0 } traceParent
            && ActivityContext.TryParse(traceParent, null, out var origin))
        {
            return [new ActivityLink(origin)];
        }

        return [];
    }
}
