using Ledger.Application.Resilience;

namespace Ledger.Application.Tests.Resilience;

[Trait("Category", "Unit")]
public sealed class ExponentialBackoffTests
{
    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Maximum = TimeSpan.FromSeconds(30);

    [Fact]
    public void NextDelay_WithoutJitter_DoublesFromTheMinimumUpToTheMaximum()
    {
        var backoff = new ExponentialBackoff(Minimum, Maximum, 0, () => 0.5);

        var delays = Enumerable.Range(0, 7).Select(_ => backoff.NextDelay().TotalSeconds).ToList();

        delays.ShouldBe([1d, 2d, 4d, 8d, 16d, 30d, 30d]);
    }

    [Fact]
    public void NextDelay_WithTwentyPercentJitter_StaysWithinTheBandAroundEachStep()
    {
        var steps = new[] { 1d, 2d, 4d, 8d, 16d, 30d, 30d };

        foreach (var unit in new[] { 0d, 0.25, 0.5, 0.75, 1d })
        {
            var backoff = new ExponentialBackoff(Minimum, Maximum, 20, () => unit);

            foreach (var step in steps)
            {
                var delay = backoff.NextDelay().TotalSeconds;

                delay.ShouldBeGreaterThanOrEqualTo(Minimum.TotalSeconds);
                delay.ShouldBeGreaterThanOrEqualTo(step * 0.8 - 1e-9);
                delay.ShouldBeLessThanOrEqualTo((step * 1.2) + 1e-9);
            }
        }
    }

    [Fact]
    public void NextDelay_NeverFallsBelowTheMinimumEvenWithNegativeJitter()
    {
        var backoff = new ExponentialBackoff(Minimum, Maximum, 20, () => 0d);

        backoff.NextDelay().ShouldBe(Minimum);
    }

    [Fact]
    public void NextDelay_AtTheMaximumWithPositiveJitter_MayExceedItByTheJitterOnly()
    {
        var backoff = new ExponentialBackoff(Minimum, Maximum, 20, () => 1d);

        var delays = Enumerable.Range(0, 12).Select(_ => backoff.NextDelay()).ToList();

        delays[^1].ShouldBe(TimeSpan.FromSeconds(36));
        delays.ShouldAllBe(delay => delay <= TimeSpan.FromSeconds(36));
    }

    [Fact]
    public void Reset_ReturnsTheNextDelayToTheMinimum()
    {
        var backoff = new ExponentialBackoff(Minimum, Maximum, 0, () => 0.5);

        backoff.NextDelay();
        backoff.NextDelay();
        backoff.NextDelay();
        backoff.Reset();

        backoff.Attempts.ShouldBe(0);
        backoff.NextDelay().ShouldBe(Minimum);
    }

    [Fact]
    public void Attempts_CountsTheDelaysHandedOut()
    {
        var backoff = new ExponentialBackoff(Minimum, Maximum, 0, () => 0.5);

        backoff.NextDelay();
        backoff.NextDelay();

        backoff.Attempts.ShouldBe(2);
    }

    [Fact]
    public void Constructor_RejectsAMaximumBelowTheMinimum()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ExponentialBackoff(Maximum, Minimum, 0, () => 0.5));
    }

    [Fact]
    public void Constructor_RejectsAZeroMinimum()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ExponentialBackoff(TimeSpan.Zero, Maximum, 0, () => 0.5));
    }

    [Fact]
    public void Constructor_RejectsNegativeJitter()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ExponentialBackoff(Minimum, Maximum, -1, () => 0.5));
    }
}
