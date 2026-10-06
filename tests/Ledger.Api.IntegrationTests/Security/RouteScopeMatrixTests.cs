using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class RouteScopeMatrixTests : IDisposable
{
    private readonly TestApiFactory _factory = TestApiFactory.With(new Dictionary<string, string?>
    {
        ["Authorization:AccountProvisioningClients:0"] = "*"
    });

    public static TheoryData<int> RouteIndexes
    {
        get
        {
            var data = new TheoryData<int>();

            for (var index = 0; index < RouteCatalog.BusinessRoutes.Count; index++)
            {
                data.Add(index);
            }

            return data;
        }
    }

    public void Dispose() => _factory.Dispose();

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithoutAToken_EveryBusinessRoute_Answers401(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, token: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route.Template);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithAMalformedToken_EveryBusinessRoute_Answers401(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, "this-is-not-a-jwt");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, route.Template);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithTheReadScopeOnly_TheWriteRoutesAreForbiddenAndTheReadRoutesPassTheAuthorization(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac(scope: "ledger.read"));

        AssertOutcome(route, response, passes: !route.IsWrite);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithTheWriteScopeOnly_TheReadRoutesAreForbiddenAndTheWriteRoutesPassTheAuthorization(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac(scope: "ledger.write"));

        AssertOutcome(route, response, passes: route.IsWrite);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithBothScopes_EveryBusinessRoutePassesTheAuthorization(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac());

        AssertOutcome(route, response, passes: true);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithATokenWithoutClientId_EveryBusinessRouteIsForbidden(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac(clientId: null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, route.Template);
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithAClientIdThatDoesNotFitTheColumn_EveryBusinessRouteIsForbiddenAndNeverFails(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac(clientId: new string('c', 5000)));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, route.Template);
        response.Headers.WwwAuthenticate.ToString().ShouldBe("Bearer error=\"insufficient_scope\"");
    }

    [Theory]
    [MemberData(nameof(RouteIndexes))]
    public async Task WithAClientIdOfExactlyTheColumnSize_EveryBusinessRoutePassesTheAuthorization(int index)
    {
        var route = RouteCatalog.BusinessRoutes[index];

        using var response = await SendAsync(route, TokenForge.Hmac(clientId: new string('c', 128)));

        AssertOutcome(route, response, passes: true);
    }

    private static void AssertOutcome(BusinessRoute route, HttpResponseMessage response, bool passes)
    {
        if (passes)
        {
            response.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, route.Template);
            response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, route.Template);
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, route.Template);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(BusinessRoute route, string? token)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(route.Method, route.Path);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (route.Method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Headers.Add("Idempotency-Key", "matrix-0001");
        }

        return await client.SendAsync(request, CancellationToken.None);
    }
}
