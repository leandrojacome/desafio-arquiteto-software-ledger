using Ledger.Api.Reads;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Unit")]
public sealed class ReadOptionsValidationTests
{
    [Fact]
    public void StatementOptions_WithoutConfiguration_UseTheContractDefaults()
    {
        var options = ResolveStatement([]);

        options.DefaultLimit.ShouldBe(50);
        options.MaxLimit.ShouldBe(200);
    }

    [Theory]
    [InlineData("MaxLimit", "0")]
    [InlineData("MaxLimit", "201")]
    [InlineData("MaxLimit", "-1")]
    [InlineData("DefaultLimit", "0")]
    [InlineData("DefaultLimit", "201")]
    public void StatementOptions_WithAValueOutOfRange_AreRefusedAndTheMessageNamesTheKey(string key, string value)
    {
        var failures = StatementFailures(new Dictionary<string, string?> { [$"Ledger:Statement:{key}"] = value });

        failures.ShouldContain(failure => failure.Contains("Ledger:Statement", StringComparison.Ordinal)
                                          && failure.Contains(key, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MaxLimit", "1")]
    [InlineData("MaxLimit", "200")]
    [InlineData("DefaultLimit", "1")]
    [InlineData("DefaultLimit", "200")]
    public void StatementOptions_AtTheEdgesOfTheRange_AreAccepted(string key, string value)
    {
        var values = new Dictionary<string, string?> { [$"Ledger:Statement:{key}"] = value };

        if (key == "MaxLimit")
        {
            values["Ledger:Statement:DefaultLimit"] = "1";
        }

        var options = ResolveStatement(values);

        (key == "MaxLimit" ? options.MaxLimit : options.DefaultLimit).ShouldBe(int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void StatementOptions_WithTheDefaultAboveTheMaximum_AreRefused()
    {
        var failures = StatementFailures(new Dictionary<string, string?>
        {
            ["Ledger:Statement:DefaultLimit"] = "100",
            ["Ledger:Statement:MaxLimit"] = "99"
        });

        failures.ShouldContain(failure => failure.Contains("Ledger:Statement", StringComparison.Ordinal)
                                          && failure.Contains("DefaultLimit must not exceed MaxLimit", StringComparison.Ordinal));
    }

    [Fact]
    public void StatementOptions_WithTheDefaultEqualToTheMaximum_AreAccepted()
    {
        var options = ResolveStatement(new Dictionary<string, string?>
        {
            ["Ledger:Statement:DefaultLimit"] = "30",
            ["Ledger:Statement:MaxLimit"] = "30"
        });

        options.DefaultLimit.ShouldBe(30);
        options.MaxLimit.ShouldBe(30);
    }

    [Fact]
    public void StatementOptions_WithAnInvalidValue_StopTheProcessAtStartup()
    {
        using var provider = StatementProvider(new Dictionary<string, string?> { ["Ledger:Statement:MaxLimit"] = "201" });

        var validator = provider.GetRequiredService<IStartupValidator>();

        Should.Throw<OptionsValidationException>(validator.Validate);
    }

    [Fact]
    public void SettlingWindow_WithoutConfiguration_IsFiveSeconds()
    {
        ResolveBalance([]).SettlingWindowSeconds.ShouldBe(5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("-3")]
    public void SettlingWindow_OutOfRange_IsRefusedAndTheMessageNamesTheKey(string value)
    {
        var failures = Should.Throw<OptionsValidationException>(() =>
                ResolveBalance(new Dictionary<string, string?> { ["Ledger:Balance:SettlingWindowSeconds"] = value }))
            .Failures
            .ToList();

        failures.ShouldContain(failure => failure.Contains("Ledger:Balance", StringComparison.Ordinal)
                                          && failure.Contains("SettlingWindowSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("60", 60)]
    public void SettlingWindow_AtTheEdgesOfTheRange_IsAccepted(string value, int expected)
    {
        var options = ResolveBalance(new Dictionary<string, string?> { ["Ledger:Balance:SettlingWindowSeconds"] = value });

        options.SettlingWindowSeconds.ShouldBe(expected);
    }

    private static ServiceProvider StatementProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLedgerReads();

        return services.BuildServiceProvider();
    }

    private static StatementOptions ResolveStatement(Dictionary<string, string?> values)
    {
        using var provider = StatementProvider(values);

        return provider.GetRequiredService<IOptions<StatementOptions>>().Value;
    }

    private static List<string> StatementFailures(Dictionary<string, string?> values) =>
        [.. Should.Throw<OptionsValidationException>(() => ResolveStatement(values)).Failures];

    private static BalanceReadOptions ResolveBalance(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();

        services.AddLedgerBalanceSettings(configuration);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<BalanceReadOptions>>().Value;
    }
}
