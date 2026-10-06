using Ledger.Application.Abstractions;
using Ledger.Application.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace Ledger.Infrastructure.Messaging;

internal sealed class CircuitBreakingEventPublisher : IEventPublisher
{
    private readonly IEventPublisher _inner;
    private readonly IOutboxTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CircuitBreakingEventPublisher> _logger;
    private readonly TimeSpan _breakDuration;
    private readonly ResiliencePipeline _pipeline;
    private readonly Lock _gate = new();
    private BrokerCircuitState _state = BrokerCircuitState.Closed;

    public CircuitBreakingEventPublisher(
        IEventPublisher inner,
        IOptions<BrokerCircuitOptions> options,
        IOutboxTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<CircuitBreakingEventPublisher> logger)
    {
        _inner = inner;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _logger = logger;
        _breakDuration = TimeSpan.FromSeconds(options.Value.BreakSeconds);
        _pipeline = BuildPipeline(options.Value);
    }

    public BrokerCircuitState Circuit
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsConnected => _inner.IsConnected;

    public int ClaimBudget(int configuredBatchSize)
    {
        var connectedBudget = _inner.ClaimBudget(configuredBatchSize);

        lock (_gate)
        {
            return _state == BrokerCircuitState.Closed ? connectedBudget : 0;
        }
    }

    public Task<bool> TryConnectAsync(CancellationToken cancellationToken) =>
        _inner.TryConnectAsync(cancellationToken);

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ExecuteAsync(
                async token =>
                {
                    if (!await _inner.ProbeAsync(token))
                    {
                        throw new EventPublishException(PublishFailureReason.BrokerUnavailable, Guid.Empty);
                    }
                },
                cancellationToken);

            return true;
        }
        catch (BrokenCircuitException)
        {
            return false;
        }
        catch (EventPublishException)
        {
            return false;
        }
    }

    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            await _pipeline.ExecuteAsync(
                async token => await _inner.PublishAsync(envelope, token),
                cancellationToken);
        }
        catch (BrokenCircuitException exception)
        {
            throw new EventPublishException(PublishFailureReason.BrokerUnavailable, envelope.Id, exception);
        }
    }

    private static bool CountsAsBrokerFailure(Exception? exception)
    {
        return exception is OperationCanceledException
            or EventPublishException { Reason: not (PublishFailureReason.Serialization or PublishFailureReason.Unroutable) };
    }

    private ResiliencePipeline BuildPipeline(BrokerCircuitOptions options)
    {
        var strategy = new CircuitBreakerStrategyOptions
        {
            FailureRatio = options.FailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(options.SamplingSeconds),
            MinimumThroughput = options.MinimumThroughput,
            BreakDuration = _breakDuration,
            ShouldHandle = arguments => ValueTask.FromResult(CountsAsBrokerFailure(arguments.Outcome.Exception)),
            OnOpened = _ =>
            {
                Move(BrokerCircuitState.Open);

                return default;
            },
            OnHalfOpened = _ =>
            {
                Move(BrokerCircuitState.HalfOpen);

                return default;
            },
            OnClosed = _ =>
            {
                Move(BrokerCircuitState.Closed);

                return default;
            }
        };

        return new ResiliencePipelineBuilder { TimeProvider = _timeProvider }
            .AddCircuitBreaker(strategy)
            .Build();
    }

    private void Move(BrokerCircuitState next)
    {
        BrokerCircuitState previous;

        lock (_gate)
        {
            previous = _state;
            _state = next;
        }

        if (previous == next)
        {
            return;
        }

        _telemetry.CircuitStateChanged(next);
        MessagingLog.OutboxCircuitStateChanged(_logger, previous.ToString(), next.ToString());
    }
}
