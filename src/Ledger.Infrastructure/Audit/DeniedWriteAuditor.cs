using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Audit;

internal sealed partial class DeniedWriteAuditor : IDeniedWriteAuditor, IAsyncDisposable
{
    public const string RateCappedReason = "rate_capped";
    public const string WriteFailedReason = "write_failed";

    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IdleBucketLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownPoll = TimeSpan.FromMilliseconds(10);

    private readonly IAuditTrail _trail;
    private readonly ISecurityTelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly ILogger<DeniedWriteAuditor> _logger;
    private readonly int _capacity;
    private readonly int _refillPerSecond;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private long _lastSweep;
    private int _inFlight;

    public DeniedWriteAuditor(
        IAuditTrail trail,
        ISecurityTelemetry telemetry,
        IOptions<DeniedWriteAuditOptions> options,
        TimeProvider time,
        ILogger<DeniedWriteAuditor> logger)
    {
        _trail = trail;
        _telemetry = telemetry;
        _time = time;
        _logger = logger;
        _capacity = options.Value.Capacity;
        _refillPerSecond = options.Value.RefillPerSecond;
        _lastSweep = time.GetTimestamp();
    }

    internal int TrackedClients => _buckets.Count;

    internal int PendingWrites => Volatile.Read(ref _inFlight);

    public void Record(
        string clientId,
        AccountId? accountId,
        string correlationId,
        string route,
        DeniedWriteReason reason)
    {
        var now = _time.GetTimestamp();

        SweepIdleBuckets(now);

        if (!TryTakeToken(clientId, now))
        {
            LogSkipped(_logger, RateCappedReason, clientId);
            _telemetry.AuditSkipped(RateCappedReason);

            return;
        }

        Interlocked.Increment(ref _inFlight);
        _ = WriteAsync(clientId, accountId, correlationId, route, reason);
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(ShutdownWait, _time);

        try
        {
            while (PendingWrites > 0)
            {
                await Task.Delay(ShutdownPoll, _time, deadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    [LoggerMessage(
        EventId = 6003,
        EventName = "DeniedWriteAuditSkipped",
        Level = LogLevel.Warning,
        Message = "A denied write was not recorded in the audit trail ({Reason}) for client {ClientId}")]
    private static partial void LogSkipped(ILogger logger, string reason, string clientId);

    [LoggerMessage(
        EventId = 6004,
        EventName = "DeniedWriteAuditFailed",
        Level = LogLevel.Warning,
        Message = "A denied write could not be recorded in the audit trail ({ExceptionType})")]
    private static partial void LogFailed(ILogger logger, string exceptionType);

    private bool TryTakeToken(string clientId, long now)
    {
        var bucket = _buckets.GetOrAdd(clientId, _ => new Bucket(_capacity, now));

        return bucket.TryTake(now, _capacity, _refillPerSecond, _time);
    }

    private void SweepIdleBuckets(long now)
    {
        var lastSweep = Interlocked.Read(ref _lastSweep);

        if (_time.GetElapsedTime(lastSweep, now) < SweepInterval
            || Interlocked.CompareExchange(ref _lastSweep, now, lastSweep) != lastSweep)
        {
            return;
        }

        foreach (var (clientId, bucket) in _buckets)
        {
            if (_time.GetElapsedTime(bucket.LastTouched, now) >= IdleBucketLifetime)
            {
                _buckets.TryRemove(clientId, out _);
            }
        }
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "The denial is best effort: any failure is logged and counted and never reaches the response.")]
    private async Task WriteAsync(
        string clientId,
        AccountId? accountId,
        string correlationId,
        string route,
        DeniedWriteReason reason)
    {
        try
        {
            await Task.Yield();

            var auditEvent = AuditEvents.AuthorizationDenied(clientId, accountId, correlationId, route, reason);

            using var deadline = new CancellationTokenSource(WriteTimeout, _time);
            await _trail.RecordAsync(auditEvent, deadline.Token);
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception.GetType().Name);
            _telemetry.AuditSkipped(WriteFailedReason);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private sealed class Bucket(int capacity, long timestamp)
    {
        private readonly Lock _gate = new();
        private double _tokens = capacity;
        private long _timestamp = timestamp;

        public long LastTouched
        {
            get
            {
                lock (_gate)
                {
                    return _timestamp;
                }
            }
        }

        public bool TryTake(long now, int maxTokens, int refillPerSecond, TimeProvider time)
        {
            lock (_gate)
            {
                var elapsedSeconds = time.GetElapsedTime(_timestamp, now).TotalSeconds;
                _tokens = Math.Min(maxTokens, _tokens + (elapsedSeconds * refillPerSecond));
                _timestamp = now;

                if (_tokens < 1)
                {
                    return false;
                }

                _tokens -= 1;

                return true;
            }
        }
    }
}
