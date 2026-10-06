using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
[Trait("Category", "Security")]
public sealed class ReadAuthorizationTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    public static TheoryData<string> Routes =>
    [
        $"/v1/accounts/{Account}/balance",
        $"/v1/accounts/{Account}/entries"
    ];

    private static string Invalid(string route) =>
        route.EndsWith("balance", StringComparison.Ordinal) ? $"{route}?asOf=abc" : $"{route}?limit=0";

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithoutAToken_Returns401Unauthenticated(string route)
    {
        using var client = ReadApiClient.Anonymous(factory);

        using var response = await client.GetAsync(Invalid(route));

        response.ShouldBeProblem(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Autenticação necessária", route);
        response.HasHeader("WWW-Authenticate").ShouldBeTrue();
        response.ShouldCarryNoInternals();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithAnInvalidToken_Returns401Unauthenticated(string route)
    {
        using var client = ReadApiClient.For(factory, TestTokenFactory.Create(signingKey: "another-signing-key-0123456789-abcdef"));

        using var response = await client.GetAsync(Invalid(route));

        response.ShouldBeProblem(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Autenticação necessária");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithATokenThatOnlyWrites_Returns403Forbidden(string route)
    {
        using var client = ReadApiClient.For(factory, TestTokenFactory.Create("ledger.write"));

        using var response = await client.GetAsync(Invalid(route));

        response.ShouldBeProblem(HttpStatusCode.Forbidden, "FORBIDDEN", "Acesso negado", route);
        response.ShouldCarryNoInternals();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithAValidTokenWithoutClientId_Returns403Forbidden(string route)
    {
        using var client = ReadApiClient.For(factory, TestTokenFactory.Create("ledger.read", clientId: null));

        using var response = await client.GetAsync(Invalid(route));

        response.ShouldBeProblem(HttpStatusCode.Forbidden, "FORBIDDEN", "Acesso negado");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithTheReadScope_PassesAuthorization(string route)
    {
        using var client = ReadApiClient.For(factory, TestTokenFactory.Create("ledger.read"));

        using var response = await client.GetAsync(Invalid(route));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_WithBothScopes_PassesAuthorization(string route)
    {
        using var client = ReadApiClient.For(factory, TestTokenFactory.Create("ledger.read ledger.write"));

        using var response = await client.GetAsync(Invalid(route));

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Body);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Read_RefusedByTheAuthorization_NeverTouchesTheDatabase(string route)
    {
        using var anonymous = ReadApiClient.Anonymous(factory);
        using var writer = ReadApiClient.For(factory, TestTokenFactory.Create("ledger.write"));

        using var withoutToken = await anonymous.GetAsync(route);
        using var withTheWrongScope = await writer.GetAsync(route);

        withoutToken.Status.ShouldBe(HttpStatusCode.Unauthorized);
        withTheWrongScope.Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
