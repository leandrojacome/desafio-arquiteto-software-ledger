using System.Security.Claims;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Unit")]
public sealed class ProvisioningOptionsValidationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void AnEmptyList_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate([], environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Authorization:AccountProvisioningClients");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public void TheWildcard_IsRefusedOutsideDevelopmentAndTesting(string environment)
    {
        var result = Validate(["billing-core", "*"], environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("wildcard");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AnExplicitList_IsAcceptedOutsideDevelopmentAndTesting(string environment)
    {
        Validate(["billing-core", "back-office"], environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void TheWildcardAndTheEmptyList_AreAcceptedInDevelopmentAndTesting(string environment)
    {
        Validate(["*"], environment).Succeeded.ShouldBeTrue();
        Validate([], environment).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ABlankEntry_IsAlwaysRefused(string environment)
    {
        var result = Validate(["billing-core", " "], environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("blank");
    }

    [Theory]
    [InlineData("billing-core", true)]
    [InlineData("Billing-Core", false)]
    [InlineData("billing-core-2", false)]
    [InlineData("pix-core", false)]
    public async Task TheHandler_AcceptsOnlyAClientOfTheList(string clientId, bool expected)
    {
        var handler = new ProvisioningClientHandler(Options.Create(Provisioning(["billing-core", "back-office"])));

        (await Evaluate(handler, clientId)).ShouldBe(expected);
    }

    [Fact]
    public async Task TheHandler_WithTheWildcard_AcceptsAnyClient()
    {
        var handler = new ProvisioningClientHandler(Options.Create(Provisioning(["*"])));

        (await Evaluate(handler, "anyone")).ShouldBeTrue();
    }

    [Fact]
    public async Task TheHandler_WithoutAClientId_DeniesEvenWithTheWildcard()
    {
        var handler = new ProvisioningClientHandler(Options.Create(Provisioning(["*"])));

        (await Evaluate(handler, null)).ShouldBeFalse();
    }

    [Fact]
    public async Task TheHandler_WithAnEmptyList_DeniesEveryone()
    {
        var handler = new ProvisioningClientHandler(Options.Create(Provisioning([])));

        (await Evaluate(handler, "billing-core")).ShouldBeFalse();
    }

    private static async Task<bool> Evaluate(ProvisioningClientHandler handler, string? clientId)
    {
        var claims = new List<Claim>();

        if (clientId is not null)
        {
            claims.Add(new Claim(ClaimNames.ClientId, clientId));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        var requirement = new ProvisioningClientRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private static ProvisioningOptions Provisioning(string[] clients) =>
        new() { AccountProvisioningClients = clients };

    private static ValidateOptionsResult Validate(string[] clients, string environment)
    {
        var validator = new ProvisioningOptionsValidator(new TestHostEnvironment(environment));

        return validator.Validate(null, Provisioning(clients));
    }
}
