using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

[Trait("Category", "E2E")]
public sealed class E2ETokenFactoryTests
{
    [Fact]
    public void Create_WithAnExplicitSigner_ProducesAVerifiableRs256TokenWithTheRequestedScopes()
    {
        using var signer = RSA.Create(2048);

        var token = E2ETokenFactory.Create(E2ETokenFactory.ReadOnly, "pix-gateway", signer: signer);

        var parts = token.Split('.');
        var header = JsonDocument.Parse(Decode(parts[0])).RootElement;
        var payload = JsonDocument.Parse(Decode(parts[1])).RootElement;

        parts.Length.ShouldBe(3);
        header.GetProperty("alg").GetString().ShouldBe("RS256");
        header.GetProperty("kid").GetString().ShouldBe("dev-local");
        payload.GetProperty("scope").GetString().ShouldBe("ledger.read");
        payload.GetProperty("client_id").GetString().ShouldBe("pix-gateway");
        payload.GetProperty("aud").GetString().ShouldBe(E2ETokenFactory.DefaultAudience);
        signer.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1).ShouldBeTrue();
    }

    [Fact]
    public void Create_WithAnAge_IssuesATokenThatAlreadyExpired()
    {
        using var signer = RSA.Create(2048);

        var token = E2ETokenFactory.Create(age: TimeSpan.FromHours(2), lifetime: TimeSpan.FromHours(1), signer: signer);

        var payload = JsonDocument.Parse(Decode(token.Split('.')[1])).RootElement;
        var expires = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());

        expires.ShouldBeLessThan(TimeProvider.System.GetUtcNow().AddMinutes(-30));
    }

    private static string Decode(string part) => Encoding.UTF8.GetString(Base64Url(part));

    private static byte[] Base64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}
