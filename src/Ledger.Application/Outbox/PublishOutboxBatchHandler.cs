using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ledger.Application.Outbox;

public sealed class PublishOutboxBatchHandler(
    IOutboxQueue queue,
    IEventPublisher publisher,
    IOutboxTelemetry telemetry,
    OutboxSettings settings,
    TimeProvider timeProvider,
    ILogger<PublishOutboxBatchHandler> logger)
{
    public async Task<PublishOutboxBatchOutcome> HandleAsync(
        PublishOutboxBatchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!publisher.IsConnected && !await publisher.TryConnectAsync(cancellationToken))
        {
            return PublishOutboxBatchOutcome.NotClaimed(publisher.Circuit);
        }

        if (publisher.Circuit != BrokerCircuitState.Closed && !await publisher.ProbeAsync(cancellationToken))
        {
            return PublishOutboxBatchOutcome.NotClaimed(publisher.Circuit);
        }

        var budget = publisher.ClaimBudget(settings.BatchSize);

        if (budget <= 0)
        {
            return PublishOutboxBatchOutcome.NotClaimed(publisher.Circuit);
        }

        using var poll = telemetry.BeginPoll();

        var batch = await queue.ClaimBatchAsync(budget, settings.Lease, cancellationToken);
        var ordered = batch.OrderBy(message => message.CreatedAt).ToList();

        poll.BatchSize(ordered.Count);
        ReportStuckMessages(ordered);

        var batchResult = new BatchResult();

        try
        {
            await PublishAllAsync(ordered, batchResult, cancellationToken);
        }
        finally
        {
            try
            {
                await MarkConfirmedWithOwnDeadlineAsync(batchResult.Confirmed);
            }
            finally
            {
                await ReleaseUnattemptedWithOwnDeadlineAsync(batchResult.Unattempted);
            }
        }

        ReportUnroutable(batchResult.Unroutable);

        OutboxLog.OutboxBatchPublished(
            logger,
            ordered.Count == 0 ? LogLevel.Trace : LogLevel.Debug,
            ordered.Count,
            batchResult.Confirmed.Count);

        return PublishOutboxBatchOutcome.Completed(ordered.Count, batchResult.Confirmed.Count, publisher.Circuit);
    }

    private async Task PublishAllAsync(
        IReadOnlyList<OutboxEnvelope> ordered,
        BatchResult batchResult,
        CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(settings.ConfirmTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        var publications = ordered.Select(envelope =>
            PublishOneAsync(envelope, batchResult, linked.Token, deadline.Token, cancellationToken));

        await Task.WhenAll(publications);
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Whatever a publication throws is a failure of that one message and must not take the batch down.")]
    private async Task PublishOneAsync(
        OutboxEnvelope envelope,
        BatchResult batchResult,
        CancellationToken publishToken,
        CancellationToken deadlineToken,
        CancellationToken serviceToken)
    {
        using var operation = telemetry.BeginPublish(envelope);

        try
        {
            await publisher.PublishAsync(envelope, publishToken);

            operation.Confirmed();
            batchResult.Confirmed.Enqueue(envelope.Id);
        }
        catch (EventPublishException exception)
        {
            Fail(operation, envelope, exception.Reason, batchResult);
        }
        catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested
                                                 && !serviceToken.IsCancellationRequested)
        {
            Fail(operation, envelope, PublishFailureReason.Timeout, batchResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(operation, envelope, PublishFailureReason.BrokerUnavailable, batchResult);
        }
    }

    private void Fail(
        IOutboxPublishOperation operation,
        OutboxEnvelope envelope,
        PublishFailureReason reason,
        BatchResult batchResult)
    {
        if (reason is PublishFailureReason.BrokerUnavailable or PublishFailureReason.Timeout
            or PublishFailureReason.Unroutable)
        {
            batchResult.Unattempted.Enqueue(envelope.Id);
        }

        operation.Failed(reason);
        telemetry.PublishFailed(reason);

        if (reason == PublishFailureReason.Unroutable)
        {
            batchResult.Unroutable.Enqueue(envelope.Id);

            return;
        }

        OutboxLog.OutboxPublishFailed(logger, envelope.Id, reason.ToLabel(), envelope.Attempts);
    }

    private void ReportUnroutable(ConcurrentQueue<Guid> unroutable)
    {
        if (!unroutable.IsEmpty)
        {
            OutboxLog.OutboxBatchUnroutable(logger, unroutable.Count);
        }
    }

    private async Task MarkConfirmedWithOwnDeadlineAsync(ConcurrentQueue<Guid> confirmed)
    {
        if (confirmed.IsEmpty)
        {
            return;
        }

        var ids = confirmed.ToArray();

        using var deadline = new CancellationTokenSource(settings.Lease, timeProvider);

        await queue.MarkPublishedAsync(ids, deadline.Token);

        telemetry.Published(ids.Length);
    }

    private async Task ReleaseUnattemptedWithOwnDeadlineAsync(ConcurrentQueue<Guid> unattempted)
    {
        if (unattempted.IsEmpty)
        {
            return;
        }

        using var deadline = new CancellationTokenSource(settings.Lease, timeProvider);

        await queue.ReleaseAsync(unattempted.ToArray(), deadline.Token);
    }

    private void ReportStuckMessages(IReadOnlyList<OutboxEnvelope> ordered)
    {
        foreach (var envelope in ordered.Where(message => message.Attempts >= settings.FailedAttempts))
        {
            OutboxLog.OutboxMessageStuck(logger, envelope.Id, envelope.Attempts);
        }
    }

    private sealed class BatchResult
    {
        public ConcurrentQueue<Guid> Confirmed { get; } = new();

        public ConcurrentQueue<Guid> Unattempted { get; } = new();

        public ConcurrentQueue<Guid> Unroutable { get; } = new();
    }
}
