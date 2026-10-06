using System.Diagnostics.CodeAnalysis;

namespace Ledger.Application.Resilience;

public sealed class ExponentialBackoff
{
    private const int MaxDoublings = 20;

    private readonly Lock _gate = new();
    private readonly TimeSpan _minimum;
    private readonly TimeSpan _maximum;
    private readonly double _jitter;
    private readonly Func<double> _nextUnit;
    private int _attempts;

    public ExponentialBackoff(TimeSpan minimum, TimeSpan maximum, int jitterPercent)
        : this(minimum, maximum, jitterPercent, NextRandomUnit)
    {
    }

    public ExponentialBackoff(TimeSpan minimum, TimeSpan maximum, int jitterPercent, Func<double> nextUnit)
    {
        ArgumentNullException.ThrowIfNull(nextUnit);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimum, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, minimum);
        ArgumentOutOfRangeException.ThrowIfNegative(jitterPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jitterPercent, 100);

        _minimum = minimum;
        _maximum = maximum;
        _jitter = jitterPercent / 100d;
        _nextUnit = nextUnit;
    }

    public int Attempts
    {
        get
        {
            lock (_gate)
            {
                return _attempts;
            }
        }
    }

    public TimeSpan NextDelay()
    {
        lock (_gate)
        {
            var doublings = Math.Min(_attempts, MaxDoublings);
            var scaled = TimeSpan.FromTicks(_minimum.Ticks * (1L << doublings));
            var baseDelay = scaled > _maximum ? _maximum : scaled;
            var factor = 1d + (_jitter * ((2d * Math.Clamp(_nextUnit(), 0d, 1d)) - 1d));
            var jittered = TimeSpan.FromTicks((long)(baseDelay.Ticks * factor));

            _attempts++;

            return jittered < _minimum ? _minimum : jittered;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _attempts = 0;
        }
    }

    [SuppressMessage("Security", "CA5394",
        Justification = "The jitter only spreads retries in time and has no security impact.")]
    private static double NextRandomUnit() => Random.Shared.NextDouble();
}
