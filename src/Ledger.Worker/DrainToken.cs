namespace Ledger.Worker;

internal sealed class DrainToken : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly CancellationTokenRegistration _registration;
    private ITimer? _timer;
    private bool _disposed;

    public DrainToken(TimeSpan grace, TimeProvider timeProvider, CancellationToken stopping)
    {
        _registration = stopping.Register(() => StartGrace(grace, timeProvider));
    }

    public CancellationToken Token => _source.Token;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _registration.Dispose();
        _timer?.Dispose();
        _source.Dispose();
    }

    private void StartGrace(TimeSpan grace, TimeProvider timeProvider)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer = timeProvider.CreateTimer(_ => CancelSource(), null, grace, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelSource()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _source.Cancel();
        }
    }
}
