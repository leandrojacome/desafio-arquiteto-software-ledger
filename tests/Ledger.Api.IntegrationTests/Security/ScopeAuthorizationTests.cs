using System.Security.Claims;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class ScopeAuthorizationTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    [Theory]
    [InlineData("ledger.read", AuthorizationPolicies.LedgerRead, true)]
    [InlineData("ledger.read", AuthorizationPolicies.LedgerWrite, false)]
    [InlineData("ledger.write", AuthorizationPolicies.LedgerWrite, true)]
    [InlineData("ledger.write", AuthorizationPolicies.LedgerRead, false)]
    [InlineData("ledger.read ledger.write", AuthorizationPolicies.LedgerRead, true)]
    [InlineData("ledger.read ledger.write", AuthorizationPolicies.LedgerWrite, true)]
    [InlineData("ledger.reader", AuthorizationPolicies.LedgerRead, false)]
    [InlineData("", AuthorizationPolicies.LedgerRead, false)]
    public async Task Policy_DependsOnTheScopeClaim(string scope, string policy, bool expected)
    {
        var result = await AuthorizeAsync(UserWith(scope, "pix-gateway"), policy);

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(AuthorizationPolicies.LedgerRead)]
    [InlineData(AuthorizationPolicies.LedgerWrite)]
    public async Task Policy_WithoutClientId_IsDenied(string policy)
    {
        var result = await AuthorizeAsync(UserWith("ledger.read ledger.write", null), policy);

        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData(AuthorizationPolicies.LedgerRead)]
    [InlineData(AuthorizationPolicies.LedgerWrite)]
    public async Task Policy_WithAClientIdOf128Characters_IsAllowed(string policy)
    {
        var result = await AuthorizeAsync(UserWith("ledger.read ledger.write", new string('c', 128)), policy);

        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AuthorizationPolicies.LedgerRead, 129)]
    [InlineData(AuthorizationPolicies.LedgerWrite, 129)]
    [InlineData(AuthorizationPolicies.LedgerRead, 5000)]
    [InlineData(AuthorizationPolicies.LedgerWrite, 5000)]
    public async Task Policy_WithAClientIdLongerThanTheColumn_IsDenied(string policy, int length)
    {
        var result = await AuthorizeAsync(UserWith("ledger.read ledger.write", new string('c', length)), policy);

        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("pix gateway")]
    [InlineData("pix\tgateway")]
    [InlineData("pix\ngateway")]
    [InlineData("pix-gateway\u00e9")]
    [InlineData(" pix-gateway")]
    public async Task Policy_WithAClientIdOutsideVisibleAscii_IsDenied(string clientId)
    {
        var result = await AuthorizeAsync(UserWith("ledger.read ledger.write", clientId), AuthorizationPolicies.LedgerWrite);

        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("a", true)]
    [InlineData("pix-gateway", true)]
    [InlineData("svc:ledger/writer_01.v2@bank", true)]
    [InlineData("pix gateway", false)]
    [InlineData("pix\u00e9", false)]
    public void ClientIdRules_AcceptOnlyOneToOneHundredAndTwentyEightVisibleAsciiCharacters(string value, bool expected)
    {
        ClientIdRules.IsValid(value).ShouldBe(expected);
    }

    [Fact]
    public void ClientIdRules_RefuseNullAndTheLengthJustAboveTheColumn()
    {
        ClientIdRules.IsValid(null).ShouldBeFalse();
        ClientIdRules.IsValid(new string('c', ClientIdRules.MaxLength)).ShouldBeTrue();
        ClientIdRules.IsValid(new string('c', ClientIdRules.MaxLength + 1)).ShouldBeFalse();
    }

    private static ClaimsPrincipal UserWith(string scope, string? clientId)
    {
        var claims = new List<Claim> { new("scope", scope) };

        if (clientId is not null)
        {
            claims.Add(new Claim("client_id", clientId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    private async Task<bool> AuthorizeAsync(ClaimsPrincipal user, string policy)
    {
        var authorization = factory.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(user, resource: null, policy);

        return result.Succeeded;
    }
}
