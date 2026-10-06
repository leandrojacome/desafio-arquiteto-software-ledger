using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Security;

internal static class TokenForge
{
    public const string AllScopes = "ledger.read ledger.write";
    public const string DefaultClientId = "integration-tests";
    public const string AccessTokenType = "at+jwt";

    public static string Create(
        SigningCredentials? credentials,
        string scope = AllScopes,
        string? clientId = DefaultClientId,
        string audience = TestConfiguration.Audience,
        string issuer = TestConfiguration.Issuer,
        TimeSpan? expiresIn = null,
        TimeSpan? notBeforeIn = null,
        bool withoutExpiration = false,
        string? tokenType = AccessTokenType,
        TimeSpan? issuedAgo = null)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

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
            IssuedAt = now - (issuedAgo ?? TimeSpan.Zero),
            NotBefore = now + (notBeforeIn ?? TimeSpan.Zero) - (expiresIn is { } past && past < TimeSpan.Zero
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.Zero),
            Expires = withoutExpiration ? null : now + (expiresIn ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = credentials,
            TokenType = tokenType
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    public static string Hmac(
        string secret = TestConfiguration.SigningKey,
        string scope = AllScopes,
        string? clientId = DefaultClientId,
        string audience = TestConfiguration.Audience,
        string issuer = TestConfiguration.Issuer,
        TimeSpan? expiresIn = null,
        TimeSpan? notBeforeIn = null,
        bool withoutExpiration = false,
        string? tokenType = AccessTokenType,
        TimeSpan? issuedAgo = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        return Create(credentials, scope, clientId, audience, issuer, expiresIn, notBeforeIn, withoutExpiration, tokenType, issuedAgo);
    }

    public static string Unsigned(string scope = AllScopes, string? clientId = DefaultClientId)
    {
        var now = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = "none",
            ["typ"] = "JWT"
        }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = TestConfiguration.Issuer,
            ["aud"] = TestConfiguration.Audience,
            ["iat"] = now,
            ["exp"] = now + 600,
            ["scope"] = scope,
            ["client_id"] = clientId
        }));

        return $"{header}.{payload}.";
    }

    public static string Base64Url(byte[] bytes) => Base64UrlEncoder.Encode(bytes);

    public static SigningCredentials Rsa(RSA key, string keyId = "rsa-1") =>
        new(new RsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.RsaSha256);

    public static SigningCredentials Ec(ECDsa key, string keyId = "ec-1") =>
        new(new ECDsaSecurityKey(key) { KeyId = keyId }, SecurityAlgorithms.EcdsaSha256);

    public static HttpClient ClientWith(this TestApiFactory factory, string token)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
