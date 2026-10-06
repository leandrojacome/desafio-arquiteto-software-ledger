using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class JwtAuthenticationOptionsValidationTests
{
    private const string SigningKey = "unit-tests-symmetric-signing-key-0123456789";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void Authority_WithAnHttpAddress_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(Authority(authority: "http://auth.bank.internal"), environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:Authority");
        result.FailureMessage.ShouldContain("https");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void Authority_WithoutHttpsMetadata_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(Authority(requireHttpsMetadata: false), environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:RequireHttpsMetadata");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Authority_WithHttpsAndHttpsMetadata_IsAcceptedOutsideDevelopmentAndTesting(string environment)
    {
        Validate(Authority(), environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Authority_WithHttpAndWithoutHttpsMetadata_IsAcceptedInDevelopmentAndTesting(string environment)
    {
        var options = Authority(authority: "http://localhost:8180", requireHttpsMetadata: false);

        Validate(options, environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void LocalKey_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(LocalKey(), environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LocalKey");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void LocalKey_IsAcceptedInDevelopmentAndTesting(string environment)
    {
        Validate(LocalKey(), environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public void TheTokenLifetimeCeiling_OutsideOneMinuteAndOneDay_IsRefused(int minutes)
    {
        var result = Validate(LocalKey(maxTokenLifetimeMinutes: minutes), "Testing");

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:MaxTokenLifetimeMinutes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(240)]
    [InlineData(1440)]
    public void TheTokenLifetimeCeiling_InsideTheRange_IsAcceptedInDevelopmentAndTesting(int minutes)
    {
        Validate(LocalKey(maxTokenLifetimeMinutes: minutes), "Development").Succeeded.ShouldBeTrue();
        Validate(LocalKey(maxTokenLifetimeMinutes: minutes), "Testing").Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void TheTokenLifetimeCeiling_AboveAnHour_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(Authority(maxTokenLifetimeMinutes: 61), environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:MaxTokenLifetimeMinutes");
        Validate(Authority(maxTokenLifetimeMinutes: 60), environment).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void TheDefaults_AreFifteenMinutesAndTheAccessTokenType()
    {
        var options = new JwtAuthenticationOptions();

        options.MaxTokenLifetimeMinutes.ShouldBe(15);
        options.RequireAccessTokenType.ShouldBeTrue();
    }

    private static ValidateOptionsResult Validate(JwtAuthenticationOptions options, string environment)
    {
        var validator = new JwtAuthenticationOptionsValidator(new TestHostEnvironment(environment));

        return validator.Validate(null, options);
    }

    private static JwtAuthenticationOptions Authority(
        string authority = "https://auth.bank.internal",
        bool requireHttpsMetadata = true,
        int maxTokenLifetimeMinutes = 15)
    {
        return new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.Authority,
            Authority = authority,
            Issuer = "https://auth.bank.internal",
            Audience = "ledger-api",
            RequireHttpsMetadata = requireHttpsMetadata,
            MaxTokenLifetimeMinutes = maxTokenLifetimeMinutes
        };
    }

    private static JwtAuthenticationOptions LocalKey(int maxTokenLifetimeMinutes = 15)
    {
        return new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            MaxTokenLifetimeMinutes = maxTokenLifetimeMinutes,
            LocalKey = new LocalKeyOptions { SigningKey = SigningKey }
        };
    }
}
