using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class RateLimitingOptionsValidationTests
{
    [Fact]
    public void Options_WithTheDefaultBuckets_AreValid()
    {
        var options = Resolve([]);

        options.WritePerClient.Capacity.ShouldBeGreaterThan(0);
        options.ReadPerClient.Capacity.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("WritePerClient")]
    [InlineData("ReadPerClient")]
    [InlineData("WritePerAccount")]
    public void Options_WithZeroCapacity_AreRejected(string bucket)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { [$"RateLimiting:{bucket}:Capacity"] = "0" });

        failures.ShouldContain(failure => failure.Contains($"RateLimiting:{bucket}") && failure.Contains("Capacity"));
    }

    [Theory]
    [InlineData("WritePerClient")]
    [InlineData("ReadPerClient")]
    [InlineData("WritePerAccount")]
    public void Options_WithZeroRefill_AreRejected(string bucket)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { [$"RateLimiting:{bucket}:RefillPerSecond"] = "0" });

        failures.ShouldContain(failure => failure.Contains($"RateLimiting:{bucket}") && failure.Contains("RefillPerSecond"));
    }

    [Theory]
    [InlineData("WritePerClient")]
    [InlineData("ReadPerClient")]
    [InlineData("WritePerAccount")]
    public void Options_WithNegativeCapacity_AreRejected(string bucket)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { [$"RateLimiting:{bucket}:Capacity"] = "-5" });

        failures.ShouldContain(failure => failure.Contains($"RateLimiting:{bucket}") && failure.Contains("Capacity"));
    }

    [Theory]
    [InlineData("WritePerClient")]
    [InlineData("ReadPerClient")]
    [InlineData("WritePerAccount")]
    public void Options_WithRefillAboveCapacity_AreRejected(string bucket)
    {
        var failures = FailuresOf(new Dictionary<string, string?>
        {
            [$"RateLimiting:{bucket}:Capacity"] = "10",
            [$"RateLimiting:{bucket}:RefillPerSecond"] = "11"
        });

        failures.ShouldContain(failure => failure.Contains($"RateLimiting:{bucket}") && failure.Contains("must not exceed"));
    }

    [Fact]
    public void Options_WithRefillEqualToCapacity_AreValid()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["RateLimiting:WritePerClient:Capacity"] = "10",
            ["RateLimiting:WritePerClient:RefillPerSecond"] = "10"
        });

        options.WritePerClient.Capacity.ShouldBe(10);
        options.WritePerClient.RefillPerSecond.ShouldBe(10);
    }

    [Fact]
    public void StartupValidation_WithZeroCapacity_StopsTheProcessBeforeTheFirstRequest()
    {
        var values = new Dictionary<string, string?> { ["RateLimiting:WritePerClient:Capacity"] = "0" };

        using var provider = ProviderFor(values);

        var validator = provider.GetRequiredService<IStartupValidator>();

        Should.Throw<OptionsValidationException>(validator.Validate);
    }

    private static ServiceProvider ProviderFor(Dictionary<string, string?> values, string environment = "Testing")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddLedgerRateLimiting();

        return services.BuildServiceProvider();
    }

    private static RateLimitingOptions Resolve(Dictionary<string, string?> values, string environment = "Testing")
    {
        using var provider = ProviderFor(values, environment);

        return provider.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
    }

    private static List<string> FailuresOf(Dictionary<string, string?> values, string environment = "Testing")
    {
        return Should.Throw<OptionsValidationException>(() => Resolve(values, environment)).Failures.ToList();
    }
}
