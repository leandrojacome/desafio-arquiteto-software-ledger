using Ledger.Infrastructure.Resilience;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class ResilienceOptionsValidatorTests
{
    [Fact]
    public void Defaults_PassAndMatchTheContract()
    {
        var options = new ResilienceOptions();

        new ResilienceOptionsValidator().Validate(null, options).Succeeded.ShouldBeTrue();
        options.Retry.MaxRetryAttempts.ShouldBe(2);
        options.Retry.BaseDelayMs.ShouldBe(50);
        options.Health.CacheSeconds.ShouldBe(5);
        options.Health.ProbeTimeoutSeconds.ShouldBe(1);
        options.RequestTimeoutSeconds.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5, true)]
    [InlineData(-1, false)]
    [InlineData(6, false)]
    public void Retry_MaxRetryAttempts_AcceptsZeroToFive(int attempts, bool accepted)
    {
        var options = new ResilienceOptions { Retry = new ResilienceRetryOptions { MaxRetryAttempts = attempts } };

        var result = new ResilienceOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBe(accepted);

        if (!accepted)
        {
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Resilience:Retry");
        }
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(1000, true)]
    [InlineData(9, false)]
    [InlineData(1001, false)]
    public void Retry_BaseDelayMs_AcceptsTenToOneThousand(int delay, bool accepted)
    {
        var options = new ResilienceOptions { Retry = new ResilienceRetryOptions { BaseDelayMs = delay } };

        var result = new ResilienceOptionsValidator().Validate(null, options);

        result.Succeeded.ShouldBe(accepted);

        if (!accepted)
        {
            result.FailureMessage.ShouldNotBeNull().ShouldContain("Resilience:Retry");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Health_ProbeTimeoutSeconds_MustBeBetweenOneAndThirty(int seconds)
    {
        var options = new ResilienceOptions { Health = new ResilienceHealthOptions { ProbeTimeoutSeconds = seconds } };

        var result = new ResilienceOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull().ShouldContain("Resilience:Health");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Health_CacheSeconds_MustBeBetweenOneAndSixty(int seconds)
    {
        var options = new ResilienceOptions { Health = new ResilienceHealthOptions { CacheSeconds = seconds } };

        var result = new ResilienceOptionsValidator().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldNotBeNull().ShouldContain("Resilience:Health");
    }
}
