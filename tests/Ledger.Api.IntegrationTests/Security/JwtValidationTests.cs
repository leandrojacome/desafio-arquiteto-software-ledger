using System.Net;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class JwtValidationTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    [Fact]
    public async Task ATokenWithValidClaims_IsAccepted()
    {
        await AssertStatusAsync(TokenForge.Hmac(), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATokenExpired20SecondsAgo_IsStillAcceptedInsideTheTolerance()
    {
        await AssertStatusAsync(TokenForge.Hmac(expiresIn: TimeSpan.FromSeconds(-20)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATokenExpired40SecondsAgo_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(expiresIn: TimeSpan.FromSeconds(-40)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenNotValidBefore20SecondsFromNow_IsAcceptedInsideTheTolerance()
    {
        await AssertStatusAsync(TokenForge.Hmac(notBeforeIn: TimeSpan.FromSeconds(20)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATokenNotValidBefore40SecondsFromNow_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(notBeforeIn: TimeSpan.FromSeconds(40)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithAlgNone_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Unsigned(), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithoutExpiration_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(withoutExpiration: true), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithTheWrongIssuer_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(issuer: "https://untrusted.example"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithTheWrongAudience_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(audience: "another-api"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenMissingItsSignaturePart_IsRefused()
    {
        await AssertStatusAsync("eyJhbGciOiJub25lIn0.eyJzY29wZSI6ImxlZGdlci53cml0ZSJ9", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenThatLivesExactlyTheCeiling_IsAccepted()
    {
        await AssertStatusAsync(TokenForge.Hmac(expiresIn: TimeSpan.FromMinutes(15)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ATokenThatLivesLongerThanTheCeiling_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(expiresIn: TimeSpan.FromMinutes(16)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenThatLivesOneYear_IsRefused()
    {
        await AssertStatusAsync(TokenForge.Hmac(expiresIn: TimeSpan.FromDays(365)), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenIssuedLongBeforeItsNotBefore_IsMeasuredFromTheIssueInstant()
    {
        var token = TokenForge.Hmac(expiresIn: TimeSpan.FromMinutes(5), issuedAgo: TimeSpan.FromHours(1));

        await AssertStatusAsync(token, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithoutIssueInstantAndWithoutNotBefore_IsRefusedBecauseItsLifetimeCannotBeMeasured()
    {
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestConfiguration.Issuer,
            Audience = TestConfiguration.Audience,
            Claims = new Dictionary<string, object> { ["scope"] = TokenForge.AllScopes, ["client_id"] = "integration-tests" },
            Expires = TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(5),
            TokenType = TokenForge.AccessTokenType,
            SigningCredentials = HmacCredentials()
        });

        await AssertStatusAsync(token, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ARefusedLongToken_IsCountedAsInvalidToken()
    {
        using var rejections = new Persistence.Support.MeterCapture("ledger.auth.failures");
        using var client = factory.ClientWith(TokenForge.Hmac(expiresIn: TimeSpan.FromHours(3)));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer error=\"invalid_token\"");
        rejections.Measurements.Select(measurement => (string)measurement.Tags["reason"]!).ToList().ShouldBe(["invalid_token"]);
    }

    [Fact]
    public async Task ATokenWithTheAccessTokenType_IsAccepted()
    {
        await AssertStatusAsync(TokenForge.Hmac(tokenType: "at+jwt"), HttpStatusCode.OK);
        await AssertStatusAsync(TokenForge.Hmac(tokenType: "application/at+jwt"), HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("id_token+jwt")]
    [InlineData("AT+JWT")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ATokenOfAnotherType_IsRefused(string? tokenType)
    {
        var token = TokenForge.Hmac(tokenType: tokenType);

        await AssertStatusAsync(token, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithTheTypeCheckOff_ATokenOfAnotherTypeIsAccepted()
    {
        using var relaxed = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Authentication:RequireAccessTokenType"] = "false"
        });
        using var client = relaxed.ClientWith(TokenForge.Hmac(tokenType: "JWT"));

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithAHigherCeiling_ALongerTokenIsAccepted()
    {
        using var relaxed = TestApiFactory.With(new Dictionary<string, string?>
        {
            ["Authentication:MaxTokenLifetimeMinutes"] = "60"
        });
        using var accepted = relaxed.ClientWith(TokenForge.Hmac(expiresIn: TimeSpan.FromMinutes(45)));
        using var refused = relaxed.ClientWith(TokenForge.Hmac(expiresIn: TimeSpan.FromMinutes(61)));

        using var inside = await accepted.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);
        using var outside = await refused.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        inside.StatusCode.ShouldBe(HttpStatusCode.OK);
        outside.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    private static SigningCredentials HmacCredentials() =>
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConfiguration.SigningKey)), SecurityAlgorithms.HmacSha256);

    private async Task AssertStatusAsync(string token, HttpStatusCode expected)
    {
        using var client = factory.ClientWith(token);

        using var response = await client.GetAsync(TestEndpointsStartupFilter.Closed, CancellationToken.None);

        response.StatusCode.ShouldBe(expected);
    }
}
