using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class LedgerOptionsValidationTests
{
    private static LedgerOptions Resolve(string? minutes)
    {
        var values = new Dictionary<string, string?>();

        if (minutes is not null)
        {
            values["Ledger:OccurredAtFutureToleranceMinutes"] = minutes;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLedgerOptions()
            .BuildServiceProvider();

        return provider.GetRequiredService<IOptions<LedgerOptions>>().Value;
    }

    [Fact]
    public void Tolerance_WhenNotConfigured_IsFiveMinutes()
    {
        Resolve(null).OccurredAtFutureToleranceMinutes.ShouldBe(5);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("60", 60)]
    public void Tolerance_WithinZeroAndSixty_IsAccepted(string text, int expected)
    {
        Resolve(text).OccurredAtFutureToleranceMinutes.ShouldBe(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("61")]
    public void Tolerance_OutsideZeroAndSixty_IsRefusedNamingTheKey(string text)
    {
        var failure = Should.Throw<OptionsValidationException>(() => Resolve(text));

        failure.Message.ShouldContain("OccurredAtFutureToleranceMinutes");
    }
}
