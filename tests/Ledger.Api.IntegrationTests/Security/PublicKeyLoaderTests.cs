using System.Security.Cryptography;
using Ledger.Api.Security;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Unit")]
public sealed class PublicKeyLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ledger-pem-{Guid.CreateVersion7():N}");

    public PublicKeyLoaderTests()
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
    public void AnRsaPublicKey_IsLoadedWithoutThePrivatePart()
    {
        using var rsa = RSA.Create(2048);

        var loaded = PublicKeyLoader.Load(Write(rsa.ExportSubjectPublicKeyInfoPem()));

        loaded.IsLoaded.ShouldBeTrue();
        loaded.Problem.ShouldBe(PublicKeyProblem.None);
        var key = loaded.Key.ShouldBeOfType<RsaSecurityKey>();

        key.PrivateKeyStatus.ShouldBe(PrivateKeyStatus.DoesNotExist);
    }

    [Fact]
    public void AnEcdsaP256PublicKey_IsLoadedWithoutThePrivatePart()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var loaded = PublicKeyLoader.Load(Write(ecdsa.ExportSubjectPublicKeyInfoPem()));

        loaded.IsLoaded.ShouldBeTrue();
        var key = loaded.Key.ShouldBeOfType<ECDsaSecurityKey>();

        Should.Throw<CryptographicException>(() => key.ECDsa.ExportParameters(includePrivateParameters: true));
    }

    [Fact]
    public void ARsaKeyBelow2048Bits_IsRefusedAsWeak()
    {
        using var rsa = RSA.Create(1024);

        PublicKeyLoader.Load(Write(rsa.ExportSubjectPublicKeyInfoPem())).Problem.ShouldBe(PublicKeyProblem.WeakKey);
    }

    [Fact]
    public void AnEcdsaKeyOnAnotherCurve_IsRefusedAsWeak()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        PublicKeyLoader.Load(Write(ecdsa.ExportSubjectPublicKeyInfoPem())).Problem.ShouldBe(PublicKeyProblem.WeakKey);
    }

    [Fact]
    public void APrivateKey_IsRefusedEvenThoughItCarriesThePublicOne()
    {
        using var rsa = RSA.Create(2048);

        PublicKeyLoader.Load(Write(rsa.ExportPkcs8PrivateKeyPem())).Problem
            .ShouldBe(PublicKeyProblem.ContainsPrivateKey);
    }

    [Fact]
    public void TextThatIsNotAKey_IsRefused()
    {
        PublicKeyLoader.Load(Write("not a pem file")).Problem.ShouldBe(PublicKeyProblem.NotAPublicKey);
    }

    [Fact]
    public void AMissingFile_IsReportedAsUnreadable()
    {
        PublicKeyLoader.Load(Path.Combine(_directory, "absent.pem")).Problem.ShouldBe(PublicKeyProblem.Unreadable);
    }

    [Fact]
    public void ADirectoryInsteadOfAFile_IsReportedAsUnreadable()
    {
        PublicKeyLoader.Load(_directory).Problem.ShouldBe(PublicKeyProblem.Unreadable);
    }

    [Fact]
    public void AFailedLoad_RefusesToHandOutAKey()
    {
        var loaded = PublicKeyLoader.Load(Write("not a pem file"));

        loaded.IsLoaded.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => loaded.Key);
    }

    private string Write(string content)
    {
        var path = Path.Combine(_directory, $"{Guid.CreateVersion7():N}.pem");

        File.WriteAllText(path, content);

        return path;
    }
}
