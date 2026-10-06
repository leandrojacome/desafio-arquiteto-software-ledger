using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.RateLimiting;
using Ledger.Api.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class ProductionGuardsTests
{
    private const string SecretLookingValue = "http://secret-looking-host.internal";

    public static TheoryData<string> StrictEnvironments => new() { "Production", "Staging" };

    public static TheoryData<string> RelaxedEnvironments => new() { "Development", "Testing" };

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void TheLocalKeyMode_IsRefused(string environment)
    {
        var result = Jwt(environment, new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://auth.bank.internal",
            Audience = "ledger-api",
            LocalKey = new LocalKeyOptions { SigningKey = new string('k', 40) }
        });

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authentication:Mode LocalKey");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AnAuthorityThatIsNotHttps_IsRefusedWithoutPrintingIt(string environment)
    {
        var result = Jwt(environment, Authority(authority: SecretLookingValue));

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authentication:Authority");
        Message(result).ShouldNotContain(SecretLookingValue);
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AMissingAuthority_IsRefused(string environment)
    {
        var result = Jwt(environment, Authority(authority: null));

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authentication:Authority");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void RequireHttpsMetadataTurnedOff_IsRefused(string environment)
    {
        var result = Jwt(environment, Authority(requireHttpsMetadata: false));

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authentication:RequireHttpsMetadata");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AMissingIssuerOrAudience_IsRefused(string environment)
    {
        var result = Jwt(environment, Authority(issuer: string.Empty, audience: string.Empty));

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authentication:Issuer");
        Message(result).ShouldContain("Authentication:Audience");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AWildcardInTheProvisioningList_IsRefused(string environment)
    {
        var result = Provisioning(environment, ["*"]);

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authorization:AccountProvisioningClients");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AnEmptyProvisioningList_IsRefused(string environment)
    {
        var result = Provisioning(environment, []);

        result.Failed.ShouldBeTrue();
        Message(result).ShouldContain("Authorization:AccountProvisioningClients");
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void AnExplicitProvisioningList_IsAccepted(string environment)
    {
        Provisioning(environment, ["billing-core"]).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void TheLimiterTurnedOff_IsRefused(string environment)
    {
        var failure = Should.Throw<OptionsValidationException>(() => RateLimiting(environment, enabled: false));

        failure.Failures.ShouldContain(text => text.Contains("RateLimiting:Enabled", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(StrictEnvironments))]
    public void TheDefaultsOfTheLimiter_AreAccepted(string environment)
    {
        RateLimiting(environment, enabled: true).Enabled.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RelaxedEnvironments))]
    public void TheSameSettings_PassInDevelopmentAndTesting(string environment)
    {
        Jwt(environment, new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            LocalKey = new LocalKeyOptions { SigningKey = new string('k', 40) }
        }).Succeeded.ShouldBeTrue();
        Jwt(environment, Authority(authority: "http://localhost:8180", requireHttpsMetadata: false)).Succeeded.ShouldBeTrue();
        Provisioning(environment, ["*"]).Succeeded.ShouldBeTrue();
        Provisioning(environment, []).Succeeded.ShouldBeTrue();
        RateLimiting(environment, enabled: false).Enabled.ShouldBeFalse();
    }

    private static string Message(ValidateOptionsResult result) => result.FailureMessage ?? string.Empty;

    private static ValidateOptionsResult Jwt(string environment, JwtAuthenticationOptions options) =>
        new JwtAuthenticationOptionsValidator(new TestHostEnvironment(environment)).Validate(null, options);

    private static ValidateOptionsResult Provisioning(string environment, string[] clients) =>
        new ProvisioningOptionsValidator(new TestHostEnvironment(environment))
            .Validate(null, new ProvisioningOptions { AccountProvisioningClients = clients });

    private static RateLimitingOptions RateLimiting(string environment, bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Enabled"] = enabled ? "true" : "false" })
            .Build();
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddLedgerRateLimiting();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
    }

    private static JwtAuthenticationOptions Authority(
        string? authority = "https://auth.bank.internal",
        bool requireHttpsMetadata = true,
        string issuer = "https://auth.bank.internal",
        string audience = "ledger-api")
    {
        return new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.Authority,
            Authority = authority,
            Issuer = issuer,
            Audience = audience,
            RequireHttpsMetadata = requireHttpsMetadata
        };
    }
}
