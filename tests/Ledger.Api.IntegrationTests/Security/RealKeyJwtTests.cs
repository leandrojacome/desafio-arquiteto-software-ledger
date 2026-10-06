using System.Net;
using System.Security.Cryptography;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class RealKeyJwtTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _otherEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ledger-jwt-{Guid.CreateVersion7():N}");

    public RealKeyJwtTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _otherRsa.Dispose();
        _ecdsa.Dispose();
        _otherEcdsa.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ARs256TokenSignedByTheConfiguredKey_IsAccepted()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Create(TokenForge.Rsa(_rsa)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnEs256TokenSignedByTheConfiguredKey_IsAccepted()
    {
        using var factory = FactoryFor(_ecdsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Create(TokenForge.Ec(_ecdsa)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnUnsignedTokenWithAlgNone_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Unsigned(), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnHs256TokenSignedWithThePublicKeyAsTheSecret_IsRefused()
    {
        var publicKeyPem = _rsa.ExportSubjectPublicKeyInfoPem();
        using var factory = FactoryFor(publicKeyPem);

        await AssertStatusAsync(factory, TokenForge.Hmac(secret: publicKeyPem), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(
            factory,
            TokenForge.Create(new SigningCredentials(
                new SymmetricSecurityKey(_rsa.ExportSubjectPublicKeyInfo()),
                SecurityAlgorithms.HmacSha256)),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ARs256TokenSignedByAnotherKey_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Create(TokenForge.Rsa(_otherRsa)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnEs256TokenSignedByAnotherKey_IsRefused()
    {
        using var factory = FactoryFor(_ecdsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Create(TokenForge.Ec(_otherEcdsa)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnRs256TokenWithTheWrongAudience_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(
            factory,
            TokenForge.Create(TokenForge.Rsa(_rsa), audience: "another-api"),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnRs256TokenWithTheWrongIssuer_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(
            factory,
            TokenForge.Create(TokenForge.Rsa(_rsa), issuer: "https://untrusted.example"),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnRs256TokenExpiredBeyondTheTolerance_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(
            factory,
            TokenForge.Create(TokenForge.Rsa(_rsa), expiresIn: TimeSpan.FromSeconds(-40)),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnRs256TokenWithoutExpiration_IsRefused()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(
            factory,
            TokenForge.Create(TokenForge.Rsa(_rsa), withoutExpiration: true),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnRs512TokenOfTheConfiguredKey_IsRefusedBecauseTheAlgorithmIsRestricted()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());
        var credentials = new SigningCredentials(new RsaSecurityKey(_rsa), SecurityAlgorithms.RsaSha512);

        await AssertStatusAsync(factory, TokenForge.Create(credentials), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnHs256TokenOfTheDevelopmentSecret_IsRefusedWhenAPublicKeyIsConfigured()
    {
        using var factory = FactoryFor(_rsa.ExportSubjectPublicKeyInfoPem());

        await AssertStatusAsync(factory, TokenForge.Hmac(), HttpStatusCode.Unauthorized);
    }

    private TestApiFactory FactoryFor(string publicKeyPem)
    {
        var path = Path.Combine(_directory, $"{Guid.CreateVersion7():N}.pem");

        File.WriteAllText(path, publicKeyPem, Encoding.ASCII);

        return TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Authentication:LocalKey:SigningKey"] = null,
            ["Authentication:LocalKey:PublicKeyPath"] = path
        });
    }

    private static async Task AssertStatusAsync(TestApiFactory factory, string token, HttpStatusCode expected)
    {
        using var client = factory.ClientWith(token);

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(expected);
    }
}
