using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Contract;

[Trait("Category", "Integration")]
[Trait("Category", "Contract")]
public sealed class OpenApiExposureTests
{
    private static readonly string[] DocumentPaths =
        ["/openapi/v1.json", "/openapi/v2.json", "/openapi/v1.yaml", "/swagger/v1/swagger.json", "/swagger"];

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task InDevelopmentAndTesting_TheDocumentIsServedWithoutAToken(string environment)
    {
        using var factory = TestApiFactory.With(Overrides(environment), environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(OpenApiDocument.Route, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/json");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OutsideDevelopmentAndTesting_NoRouteOfTheContractDocumentIsMapped(string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        using var factory = TestApiFactory.With(ProductionLikeSettings.Create(keys), environment);

        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        patterns.ShouldNotBeEmpty();
        patterns.ShouldAllBe(pattern => !pattern.Contains("openapi", StringComparison.OrdinalIgnoreCase));
        patterns.ShouldAllBe(pattern => !pattern.Contains("swagger", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task OutsideDevelopmentAndTesting_NoDocumentPathAnswersTheDocument(string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        using var factory = TestApiFactory.With(ProductionLikeSettings.Create(keys), environment);
        using var client = factory.CreateClient();

        foreach (var path in DocumentPaths)
        {
            using var response = await client.GetAsync(path, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
            (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldNotContain("\"paths\"", Case.Sensitive);
        }
    }

    [Fact]
    public async Task InTesting_AnAuthenticatedCallerOfAnUnmappedDocumentPathGetsANotFound()
    {
        using var factory = TestApiFactory.With();
        using var client = factory.Authenticated();

        using var response = await client.GetAsync("/openapi/v2.json", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Dictionary<string, string?>? Overrides(string environment) =>
        environment == "Development"
            ? new Dictionary<string, string?> { ["Authentication:LocalKey:PublicKeyPath"] = null }
            : null;
}
