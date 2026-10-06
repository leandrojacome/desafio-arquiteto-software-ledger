using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Integration")]
public sealed class ConfigurationValidationTests
{
    private const string WritePassword = "write-password-that-must-not-leak";
    private const string BalancePassword = "balance-password-that-must-not-leak";

    [Fact]
    public void AMissingPassword_RefusesToStartNamingTheKeyAndNeverTheOtherPasswords()
    {
        var settings = TestConfiguration.ForUnreachablePostgres();

        settings["Postgres:Sources:Write:Password"] = string.Empty;
        settings["Postgres:Sources:Balance:Password"] = BalancePassword;

        var failures = Refusal(settings, "Testing");

        failures.ShouldContain(failure => failure.StartsWith("Postgres:Sources:Write", StringComparison.Ordinal));
        failures.ShouldAllBe(failure => !failure.Contains(BalancePassword, StringComparison.Ordinal));
    }

    [Fact]
    public void APoolOfZero_RefusesToStartNamingTheKeyAndNeverThePassword()
    {
        var settings = TestConfiguration.ForUnreachablePostgres();

        settings["Postgres:Sources:Write:MaxPoolSize"] = "0";
        settings["Postgres:Sources:Write:Password"] = WritePassword;

        var failures = Refusal(settings, "Testing");

        failures.ShouldContain(failure => failure.Contains("Postgres:Sources:Write", StringComparison.Ordinal)
                                          && failure.Contains("MaxPoolSize", StringComparison.Ordinal));
        failures.ShouldAllBe(failure => !failure.Contains(WritePassword, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ASslModeOfDisableOutsideDevelopmentAndTesting_RefusesToStartNamingTheKeyAndNeverThePassword(
        string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        var settings = ProductionLikeSettings.Create(keys);

        settings["Postgres:SslMode"] = "Disable";
        settings["Postgres:Sources:Write:Password"] = WritePassword;

        var failures = Refusal(settings, environment);

        failures.ShouldContain(failure => failure.StartsWith("Postgres:SslMode", StringComparison.Ordinal));
        failures.ShouldAllBe(failure => !failure.Contains(WritePassword, StringComparison.Ordinal));
    }

    private static List<string> Refusal(Dictionary<string, string?> settings, string environment)
    {
        using var factory = TestApiFactory.With(settings, environment);

        var refusal = Should.Throw<Exception>(() => factory.CreateClient());

        return [.. Flatten(refusal)];
    }

    private static IEnumerable<string> Flatten(Exception exception)
    {
        switch (exception)
        {
            case OptionsValidationException validation:
                return validation.Failures;
            case AggregateException aggregate:
                return aggregate.InnerExceptions.SelectMany(Flatten);
            default:
                return exception.InnerException is null ? [exception.Message] : Flatten(exception.InnerException);
        }
    }
}
