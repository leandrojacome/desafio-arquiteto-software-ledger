using System.Diagnostics.CodeAnalysis;
using Ledger.Application;
using Ledger.Application.Abstractions;
using Ledger.Application.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledger.Infrastructure.Messaging;

internal sealed class BrokerConnection : IAsyncDisposable
{
    private const string ClientName = ServiceIdentity.Worker;

    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TopologyTimeout = TimeSpan.FromSeconds(15);

    private readonly RabbitMqOptions _options;
    private readonly ConnectionFactory _factory;
    private readonly ExponentialBackoff _backoff;
    private readonly ExponentialBackoff _unroutableBackoff;
    private readonly IOutboxTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BrokerConnection> _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private long _nextAttemptTicks;
    private int _topologyStale;
    private int _unroutableEpisode;
    private bool _disposed;

    public BrokerConnection(
        IOptions<RabbitMqOptions> options,
        IOutboxTelemetry telemetry,
        TimeProvider timeProvider,
        ILogger<BrokerConnection> logger)
    {
        _options = options.Value;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _logger = logger;
        _factory = RabbitMqConnectionFactory.Create(_options, ClientName);
        _backoff = NewBackoff(_options);
        _unroutableBackoff = NewBackoff(_options);
    }

    public bool IsConnected => Volatile.Read(ref _topologyStale) == 0 &&
                               Volatile.Read(ref _connection) is { IsOpen: true } &&
                               Volatile.Read(ref _channel) is { IsOpen: true };

    public IChannel? CurrentChannel => Volatile.Read(ref _channel) is { IsOpen: true } channel ? channel : null;

    public async Task<bool> TryConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return true;
        }

        await _connectGate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (IsConnected)
            {
                return true;
            }

            if (_timeProvider.GetUtcNow().UtcTicks < Volatile.Read(ref _nextAttemptTicks))
            {
                return false;
            }

            return await ConnectOnceAsync(cancellationToken);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public void ReportUnroutable()
    {
        if (Interlocked.Exchange(ref _topologyStale, 1) == 1)
        {
            return;
        }

        Volatile.Write(ref _unroutableEpisode, 1);
        Volatile.Write(ref _nextAttemptTicks, DeadlineAfter(_unroutableBackoff.NextDelay()));
        _telemetry.BrokerConnected(false);
    }

    public void ReportRouted()
    {
        if (Volatile.Read(ref _unroutableEpisode) == 0 || Interlocked.Exchange(ref _unroutableEpisode, 0) == 0)
        {
            return;
        }

        _unroutableBackoff.Reset();
        MessagingLog.OutboxRoutingRestored(_logger);
    }

    public async Task DiscardChannelAsync(IChannel channel)
    {
        if (!ReferenceEquals(Interlocked.CompareExchange(ref _channel, null, channel), channel))
        {
            return;
        }

        _telemetry.BrokerConnected(false);

        await CloseQuietlyAsync(channel);
    }

    public async ValueTask DisposeAsync()
    {
        await _connectGate.WaitAsync(CancellationToken.None);

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var channel = Interlocked.Exchange(ref _channel, null);
            var connection = Interlocked.Exchange(ref _connection, null);

            if (channel is not null)
            {
                await CloseQuietlyAsync(channel);
            }

            if (connection is not null)
            {
                await CloseQuietlyAsync(connection);
            }

            _telemetry.BrokerConnected(false);
        }
        finally
        {
            _connectGate.Release();
        }

        _connectGate.Dispose();
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Closing is best effort: the broker may already be gone and nothing useful can be done about a failed close.")]
    private async Task CloseQuietlyAsync(IChannel channel)
    {
        using var deadline = new CancellationTokenSource(CloseTimeout, _timeProvider);

        try
        {
            await channel.CloseAsync(deadline.Token);
            await channel.DisposeAsync();
        }
        catch (Exception)
        {
            return;
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "Closing is best effort: the broker may already be gone and nothing useful can be done about a failed close.")]
    private async Task CloseQuietlyAsync(IConnection connection)
    {
        using var deadline = new CancellationTokenSource(CloseTimeout, _timeProvider);

        try
        {
            await connection.CloseAsync(deadline.Token);
            await connection.DisposeAsync();
        }
        catch (Exception)
        {
            return;
        }
    }

    private async Task<bool> ConnectOnceAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds),
            _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        IConnection? connection = null;

        try
        {
            connection = Volatile.Read(ref _connection) is { IsOpen: true } open
                ? open
                : await OpenConnectionAsync(linked.Token);

            var topology = await DeclareTopologyAsync(connection, cancellationToken);

            var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                linked.Token);

            var previous = Interlocked.Exchange(ref _channel, channel);

            if (previous is not null)
            {
                await CloseQuietlyAsync(previous);
            }

            Volatile.Write(ref _topologyStale, 0);
            Volatile.Write(ref _nextAttemptTicks, 0);
            _backoff.Reset();
            _telemetry.BrokerConnected(true);
            MessagingLog.BrokerConnected(_logger, _options.Exchange);

            if (topology == TopologyOutcome.RetentionKeptAsFound)
            {
                MessagingLog.RetentionQueueKeptAsFound(_logger, _options.Retention.Queue);
            }

            return true;
        }
        catch (Exception exception) when (BrokerFailures.IsAttemptFailure(exception, cancellationToken))
        {
            await AbandonAsync(connection);
            RegisterFailure(exception);

            return false;
        }
    }

    private async Task<TopologyOutcome> DeclareTopologyAsync(IConnection connection, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(TopologyTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        return await RabbitMqTopology.DeclareAsync(connection, _options.Exchange, _options.Retention, linked.Token);
    }

    private static ExponentialBackoff NewBackoff(RabbitMqOptions options) =>
        new(
            TimeSpan.FromSeconds(options.ReconnectMinSeconds),
            TimeSpan.FromSeconds(options.ReconnectMaxSeconds),
            options.ReconnectJitterPercent);

    private long DeadlineAfter(TimeSpan delay) => (_timeProvider.GetUtcNow() + delay).UtcTicks;

    private async Task<IConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await _factory.CreateConnectionAsync(cancellationToken);

        connection.ConnectionShutdownAsync += (_, _) =>
        {
            if (ReferenceEquals(Volatile.Read(ref _connection), connection))
            {
                _telemetry.BrokerConnected(false);
            }

            return Task.CompletedTask;
        };

        Volatile.Write(ref _connection, connection);

        return connection;
    }

    private async Task AbandonAsync(IConnection? connection)
    {
        var channel = Interlocked.Exchange(ref _channel, null);

        if (channel is not null)
        {
            await CloseQuietlyAsync(channel);
        }

        if (connection is not null)
        {
            Interlocked.CompareExchange(ref _connection, null, connection);

            await CloseQuietlyAsync(connection);
        }
    }

    private void RegisterFailure(Exception exception)
    {
        var delay = _backoff.NextDelay();

        Volatile.Write(ref _nextAttemptTicks, DeadlineAfter(delay));
        _telemetry.BrokerConnected(false);

        MessagingLog.BrokerConnectionFailed(_logger, _backoff.Attempts, exception.GetType().Name, delay.TotalSeconds);
    }
}
