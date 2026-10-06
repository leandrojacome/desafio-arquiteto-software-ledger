using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class EndpointPolicyCoverageTests(DefaultTestApiFactory factory) : IClassFixture<DefaultTestApiFactory>
{
    private static readonly string[] HealthRoutes = ["/health/live", "/health/ready"];

    private static readonly string[] OpenApiRoutes = ["/openapi/v1.json"];

    private static readonly string[] HealthMethods = ["GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    [Fact]
    public void EveryRouteOfTheApplication_ExceptTheHealthAndTheContractOnes_AsksForAnExplicitPolicy()
    {
        var unprotected = ProductionEndpoints()
            .Where(endpoint => !HealthRoutes.Contains(endpoint.RoutePattern.RawText))
            .Where(endpoint => !OpenApiRoutes.Contains(endpoint.RoutePattern.RawText))
            .Where(endpoint => !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy is not null))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        unprotected.ShouldBeEmpty();
    }

    [Fact]
    public void TheHealthRoutes_AreAnonymous()
    {
        foreach (var route in HealthRoutes)
        {
            var endpoints = ProductionEndpoints().Where(candidate => candidate.RoutePattern.RawText == route).ToList();

            endpoints.ShouldNotBeEmpty(route);
            endpoints.ShouldAllBe(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null);
        }
    }

    [Fact]
    public void TheOpenApiRoute_IsAnonymousReadOnlyAndHasNoRequestClass()
    {
        foreach (var route in OpenApiRoutes)
        {
            var endpoint = ProductionEndpoints().Single(candidate => candidate.RoutePattern.RawText == route);

            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull(route);
            MethodsOf(endpoint).ShouldBe(["GET"]);
            endpoint.Metadata.GetMetadata<RequestClassMetadata>().ShouldBeNull(route);
        }
    }

    [Fact]
    public void TheApplication_ExposesExactlyTheBusinessRoutesOfTheContractAndTheHealthOnes()
    {
        var found = ProductionEndpoints()
            .SelectMany(endpoint => MethodsOf(endpoint).Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var expected = RouteCatalog.BusinessRoutes
            .Select(route => $"{route.Method.Method} {route.Template}")
            .Concat(HealthRoutes.SelectMany(route => HealthMethods.Select(method => $"{method} {route}")))
            .Concat(OpenApiRoutes.Select(route => $"GET {route}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        found.ShouldBe(expected);
    }

    [Fact]
    public void EveryBusinessRoute_CarriesThePolicyOfTheContract()
    {
        foreach (var route in RouteCatalog.BusinessRoutes)
        {
            var endpoint = EndpointOf(route);
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ToList();

            policies.ShouldContain(route.Policy, $"{route.Method} {route.Template}");
        }
    }

    [Fact]
    public void EveryBusinessRoute_CarriesTheRequestClassOfTheContract()
    {
        foreach (var route in RouteCatalog.BusinessRoutes)
        {
            EndpointOf(route).Metadata.GetMetadata<RequestClassMetadata>().ShouldNotBeNull($"{route.Method} {route.Template}")
                .Class.ShouldBe(route.Class, $"{route.Method} {route.Template}");
        }
    }

    [Fact]
    public void TheHealthRoutes_HaveNoRequestClass()
    {
        foreach (var route in HealthRoutes)
        {
            ProductionEndpoints().Where(candidate => candidate.RoutePattern.RawText == route)
                .ShouldAllBe(endpoint => endpoint.Metadata.GetMetadata<RequestClassMetadata>() == null);
        }
    }

    [Fact]
    public void ARouteTemplate_WithAnAccountIdParameter_UsesTheNameTheLimiterReads()
    {
        foreach (var route in RouteCatalog.BusinessRoutes.Where(candidate => candidate.Template.Contains("{accountId}", StringComparison.Ordinal)))
        {
            EndpointOf(route).RoutePattern.Parameters.Select(parameter => parameter.Name).ShouldContain("accountId");
        }
    }

    private List<RouteEndpoint> ProductionEndpoints()
    {
        return [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/__test/", StringComparison.Ordinal) == false)];
    }

    private RouteEndpoint EndpointOf(BusinessRoute route)
    {
        return ProductionEndpoints().Single(endpoint =>
            endpoint.RoutePattern.RawText == route.Template
            && MethodsOf(endpoint).Contains(route.Method.Method));
    }

    private static IEnumerable<string> MethodsOf(RouteEndpoint endpoint)
    {
        return endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
    }
}
