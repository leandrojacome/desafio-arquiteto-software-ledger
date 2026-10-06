using System.Security.Cryptography;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class JwtKeyValidationTests : IDisposable
{
    private const string ShortKey = "too-short-for-hs256";
    private const string LongKey = "a-symmetric-key-with-at-least-32-characters";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ledger-jwtkey-{Guid.CreateVersion7():N}");

    public JwtKeyValidationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ASigningKeyShorterThan32Characters_IsRefusedWithoutPrintingIt()
    {
        var result = Validate(WithSigningKey(ShortKey));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:LocalKey:SigningKey");
        result.FailureMessage.ShouldNotContain(ShortKey);
    }

    [Fact]
    public void ASigningKeyOfExactly32Characters_IsAccepted()
    {
        Validate(WithSigningKey(new string('k', 32))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APublicKeyPathThatDoesNotExist_IsRefusedNamingTheKey()
    {
        var path = Path.Combine(_directory, "missing.pem");

        var result = Validate(WithPublicKeyPath(path));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:LocalKey:PublicKeyPath");
        result.FailureMessage.ShouldContain("existing file");
        result.FailureMessage.ShouldNotContain(path);
    }

    [Fact]
    public void APublicKeyFileThatIsNotAPem_IsRefusedWithoutEchoingItsContent()
    {
        var path = WritePem("this-is-the-secret-content-of-the-file");

        var result = Validate(WithPublicKeyPath(path));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:LocalKey:PublicKeyPath");
        result.FailureMessage.ShouldNotContain("this-is-the-secret-content-of-the-file");
    }

    [Fact]
    public void APrivateKeyWhereAPublicOneIsExpected_IsRefused()
    {
        using var rsa = RSA.Create(2048);

        var result = Validate(WithPublicKeyPath(WritePem(rsa.ExportPkcs8PrivateKeyPem())));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("not a private key");
    }

    [Fact]
    public void AWeakRsaPublicKey_IsRefused()
    {
        using var rsa = RSA.Create(1024);

        var result = Validate(WithPublicKeyPath(WritePem(rsa.ExportSubjectPublicKeyInfoPem())));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("2048");
    }

    [Fact]
    public void AValidRsaPublicKey_IsAccepted()
    {
        using var rsa = RSA.Create(2048);

        Validate(WithPublicKeyPath(WritePem(rsa.ExportSubjectPublicKeyInfoPem()))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AValidEcdsaPublicKey_IsAccepted()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Validate(WithPublicKeyPath(WritePem(ecdsa.ExportSubjectPublicKeyInfoPem()))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WithBothKinds_ThePublicKeyWinsAndOnlyItIsValidated()
    {
        using var rsa = RSA.Create(2048);
        var options = new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            LocalKey = new LocalKeyOptions { SigningKey = ShortKey, PublicKeyPath = WritePem(rsa.ExportSubjectPublicKeyInfoPem()) }
        };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WithBothKinds_AnUnusablePublicKeyStillFails()
    {
        var options = new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            LocalKey = new LocalKeyOptions { SigningKey = LongKey, PublicKeyPath = WritePem("not a pem") }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:LocalKey:PublicKeyPath");
    }

    [Fact]
    public void NoKeyAtAll_IsRefused()
    {
        var options = new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false
        };

        Validate(options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void AModeOutsideTheKnownOnes_IsRefused()
    {
        var options = new JwtAuthenticationOptions
        {
            Mode = (AuthenticationMode)42,
            Issuer = "https://auth.bank.internal",
            Audience = "ledger-api"
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:Mode");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void TheLocalKeyMode_WithAValidKey_IsStillRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(WithSigningKey(LongKey), environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:Mode LocalKey");
        result.FailureMessage.ShouldNotContain(LongKey);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("development")]
    public void TheLocalKeyMode_WithAValidKey_IsAcceptedInTheRelaxedEnvironmentsOnly(string environment)
    {
        Validate(WithSigningKey(LongKey), environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AMissingIssuerOrAudience_IsRefusedNamingTheKey(string blank)
    {
        var options = new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.Authority,
            Authority = "https://auth.bank.internal",
            Issuer = blank,
            Audience = blank
        };

        var result = Validate(options, "Production");

        (result.FailureMessage ?? string.Empty).ShouldContain("Authentication:Issuer");
        (result.FailureMessage ?? string.Empty).ShouldContain("Authentication:Audience");
    }

    [Fact]
    public void AnAuthorityThatIsNotAnAbsoluteUri_IsRefused()
    {
        var options = new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.Authority,
            Authority = "auth.bank.internal",
            Issuer = "https://auth.bank.internal",
            Audience = "ledger-api"
        };

        var result = Validate(options, "Production");

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authentication:Authority");
    }

    [Fact]
    public void TheHostWithAnUnreadablePublicKey_RefusesToStartNamingTheKeyAndNotItsContent()
    {
        var path = WritePem("this-is-the-secret-content-of-the-file");
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Authentication:LocalKey:SigningKey"] = null,
            ["Authentication:LocalKey:PublicKeyPath"] = path
        });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        (failure.ToString() ?? string.Empty).ShouldContain("Authentication:LocalKey:PublicKeyPath");
        (failure.ToString() ?? string.Empty).ShouldNotContain("this-is-the-secret-content-of-the-file");
    }

    [Fact]
    public void TheHostWithAShortSigningKey_RefusesToStartNamingTheKeyAndNotItsValue()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Authentication:LocalKey:SigningKey"] = ShortKey
        });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        (failure.ToString() ?? string.Empty).ShouldContain("Authentication:LocalKey:SigningKey");
        (failure.ToString() ?? string.Empty).ShouldNotContain(ShortKey);
    }

    [Fact]
    public void TheHostWithTheLocalKeyInAStrictEnvironment_RefusesToStart()
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        var settings = ProductionLikeSettings.Create(keys);
        settings["Authentication:Mode"] = "LocalKey";
        settings["Authentication:Issuer"] = TestConfiguration.Issuer;
        using var factory = TestApiFactory.With(settings, "Production");

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        (failure.ToString() ?? string.Empty).ShouldContain("Authentication:Mode LocalKey");
    }

    [Fact]
    public void TheBearerOptions_AreBuiltAtStartupWithoutErrorDetailsAndWithTheRestrictedAlgorithms()
    {
        using var factory = TestApiFactory.With();

        var bearer = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.IncludeErrorDetails.ShouldBeFalse();
        bearer.EventsType.ShouldBe(typeof(AuthenticationEvents));
        bearer.MapInboundClaims.ShouldBeFalse();
        bearer.TokenValidationParameters.RequireSignedTokens.ShouldBeTrue();
        bearer.TokenValidationParameters.RequireExpirationTime.ShouldBeTrue();
        bearer.TokenValidationParameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));
        bearer.TokenValidationParameters.ValidAlgorithms.ShouldNotBeNull().ShouldBe(["HS256"]);
    }

    [Fact]
    public void ABearerConfigurationThatExplainsTheRejection_IsRefusedByTheStartupValidation()
    {
        var validator = new JwtBearerOptionsValidator(Options.Create(new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.Authority,
            Authority = "https://auth.bank.internal"
        }));
        var bearer = new JwtBearerOptions { IncludeErrorDetails = true, Authority = "https://auth.bank.internal" };

        var result = validator.Validate(JwtBearerDefaults.AuthenticationScheme, bearer);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("IncludeErrorDetails");
        result.FailureMessage.ShouldContain("events");
    }

    [Fact]
    public void TheBearerValidation_IgnoresTheOtherSchemes()
    {
        var validator = new JwtBearerOptionsValidator(Options.Create(new JwtAuthenticationOptions()));

        validator.Validate("Other", new JwtBearerOptions()).Skipped.ShouldBeTrue();
    }

    private static JwtAuthenticationOptions WithSigningKey(string key)
    {
        return new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            LocalKey = new LocalKeyOptions { SigningKey = key }
        };
    }

    private static JwtAuthenticationOptions WithPublicKeyPath(string path)
    {
        return new JwtAuthenticationOptions
        {
            Mode = AuthenticationMode.LocalKey,
            Issuer = "https://idp.local.test",
            Audience = "ledger-api",
            RequireHttpsMetadata = false,
            LocalKey = new LocalKeyOptions { PublicKeyPath = path }
        };
    }

    private static ValidateOptionsResult Validate(JwtAuthenticationOptions options, string environment = "Testing")
    {
        return new JwtAuthenticationOptionsValidator(new TestHostEnvironment(environment)).Validate(null, options);
    }

    private string WritePem(string content)
    {
        var path = Path.Combine(_directory, $"{Guid.CreateVersion7():N}.pem");

        File.WriteAllText(path, content);

        return path;
    }
}
