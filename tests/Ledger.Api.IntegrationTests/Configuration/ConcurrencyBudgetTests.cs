using System.Globalization;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class ConcurrencyBudgetTests
{
    private static readonly string[] ApiSources = ["Write", "Balance", "Statement"];

    private static readonly Dictionary<string, string?> DefaultPools = new()
    {
        ["Postgres:Sources:Write:MaxPoolSize"] = "7",
        ["Postgres:Sources:Balance:MaxPoolSize"] = "8",
        ["Postgres:Sources:Statement:MaxPoolSize"] = "4"
    };

    [Fact]
    public void TheDefaultLimits_SitBetweenOneAndThreeTimesTheirPools()
    {
        var options = Resolve([]);

        options.WriteConcurrency.ShouldBe(16);
        options.BalanceConcurrency.ShouldBe(16);
        options.StatementConcurrency.ShouldBe(8);
    }

    [Theory]
    [InlineData("WriteConcurrency", 22)]
    [InlineData("WriteConcurrency", 6)]
    [InlineData("BalanceConcurrency", 25)]
    [InlineData("BalanceConcurrency", 7)]
    [InlineData("StatementConcurrency", 13)]
    [InlineData("StatementConcurrency", 3)]
    public void ALimitOutsideOneToThreeTimesThePool_IsRefusedNamingTheKey(string key, int value)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { [$"RateLimiting:{key}"] = value.ToString(CultureInfo.InvariantCulture) });

        failures.ShouldContain(failure => failure.Contains($"RateLimiting:{key}") && failure.Contains("one and three times"));
    }

    [Theory]
    [InlineData("WriteConcurrency", 7)]
    [InlineData("WriteConcurrency", 21)]
    [InlineData("BalanceConcurrency", 8)]
    [InlineData("BalanceConcurrency", 24)]
    [InlineData("StatementConcurrency", 4)]
    [InlineData("StatementConcurrency", 12)]
    public void ALimitOnTheEdgeOfThePoolRange_IsAccepted(string key, int value)
    {
        var options = Resolve(new Dictionary<string, string?> { [$"RateLimiting:{key}"] = value.ToString(CultureInfo.InvariantCulture) });

        options.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("WriteConcurrency", "0")]
    [InlineData("WriteConcurrency", "-1")]
    [InlineData("WriteConcurrency", "257")]
    [InlineData("BalanceConcurrency", "0")]
    [InlineData("StatementConcurrency", "300")]
    public void ALimitOutsideOneTo256_IsRefusedNamingTheKey(string key, string value)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { [$"RateLimiting:{key}"] = value });

        failures.ShouldContain(failure => failure.Contains(key));
    }

    [Fact]
    public void ALimitThatIsNotANumber_StopsTheProcess()
    {
        Should.Throw<InvalidOperationException>(() =>
            Resolve(new Dictionary<string, string?> { ["RateLimiting:WriteConcurrency"] = "many" }));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void TurningTheLimitsOff_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var failures = FailuresOf(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "false" }, environment);

        failures.ShouldContain(failure => failure.Contains("RateLimiting:Enabled"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void TurningTheLimitsOff_IsAcceptedInDevelopmentAndTesting(string environment)
    {
        Resolve(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = "false" }, environment).Enabled.ShouldBeFalse();
    }

    [Fact]
    public void TheMessage_NeverPrintsTheValueItRefused()
    {
        var failures = FailuresOf(new Dictionary<string, string?> { ["RateLimiting:WriteConcurrency"] = "9999" });

        failures.ShouldNotContain(failure => failure.Contains("9999"));
    }

    [Fact]
    public void TheShippedSettings_PassTheValidatorInProduction()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.Source, "Ledger.Api", "appsettings.json"))
            .Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment("Production"));
        services.AddLedgerRateLimiting();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        options.Enabled.ShouldBeTrue();
        options.WritePerAccount.Capacity.ShouldBe(100);
        options.WritePerAccount.RefillPerSecond.ShouldBe(50);
        options.WriteConcurrency.ShouldBe(16);
        options.BalanceConcurrency.ShouldBe(16);
        options.StatementConcurrency.ShouldBe(8);
    }

    [Fact]
    public void ThePoolsOfTheShippedSettings_FitTheServerAtTheInstanceCeiling()
    {
        const int apiInstances = 6;
        const int workerInstances = 2;
        const int reserve = 20;
        const int serverLimit = 200;

        var api = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.Source, "Ledger.Api", "appsettings.json"))
            .Build();
        var worker = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryPaths.Source, "Ledger.Worker", "appsettings.json"))
            .Build();

        var apiPools = ApiSources
            .Sum(source => api.GetValue<int>($"Postgres:Sources:{source}:MaxPoolSize"));
        var workerPools = worker.GetValue<int>("Postgres:Sources:Worker:MaxPoolSize");
        var total = (apiInstances * apiPools) + (workerInstances * workerPools) + reserve;

        apiPools.ShouldBe(19);
        workerPools.ShouldBe(5);
        total.ShouldBe(144);
        total.ShouldBeLessThanOrEqualTo(serverLimit);
    }

    private static ServiceProvider ProviderFor(Dictionary<string, string?> values, string environment)
    {
        var merged = new Dictionary<string, string?>(DefaultPools);

        foreach (var (key, value) in values)
        {
            merged[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(merged).Build();
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
