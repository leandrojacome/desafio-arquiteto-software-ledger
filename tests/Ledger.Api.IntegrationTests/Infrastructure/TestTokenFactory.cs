using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class TestTokenFactory
{
    private const string DefaultScope = "ledger.read ledger.write";
    private const string DefaultClientId = "integration-tests";

    public static string Create(
        string scope = DefaultScope,
        string? clientId = DefaultClientId,
        string audience = TestConfiguration.Audience,
        string issuer = TestConfiguration.Issuer,
        string signingKey = TestConfiguration.SigningKey,
        TimeSpan? lifetime = null,
        bool withoutSignature = false,
        TimeSpan? notBeforeOffset = null,
        string? tokenType = "at+jwt")
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        var validFor = lifetime ?? TimeSpan.FromMinutes(10);

        var claims = new Dictionary<string, object> { ["scope"] = scope };

        if (clientId is not null)
        {
            claims["client_id"] = clientId;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = notBeforeOffset is { } offset
                ? now + offset
                : validFor < TimeSpan.Zero ? now + validFor - TimeSpan.FromMinutes(5) : now,
            Expires = now + validFor,
            IssuedAt = now,
            TokenType = tokenType,
            SigningCredentials = withoutSignature
                ? null
                : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public static HttpClient Authenticated(this LedgerApiFactory factory, string? token = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token ?? Create());

        return client;
    }
}
