using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Limits;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class ForwardedHeadersTests
{
    private const string SchemeRoute = "/__test/scheme";
    private const int AnonymousQuota = 2;

    [Fact]
    public async Task WithTheProxyOnTheList_EachOriginInTheHeaderHasItsOwnAnonymousQuota()
    {
        using var factory = new KestrelApiFactory(Settings("127.0.0.0/8", "::1/128"));
        using var client = factory.CreateKestrelClient();

        var statuses = new List<HttpStatusCode>();

        foreach (var origin in new[] { "203.0.113.1", "203.0.113.1", "203.0.113.1", "203.0.113.2", "203.0.113.3" })
        {
            statuses.Add(await StatusOfAsync(client, origin));
        }

        statuses.ShouldBe(
        [
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized
        ]);
    }

    [Fact]
    public async Task WithTheProxyOutsideTheList_TheHeaderIsIgnoredAndTheSocketOriginRules()
    {
        using var factory = new KestrelApiFactory(Settings("10.0.0.0/8"));
        using var client = factory.CreateKestrelClient();

        var statuses = new List<HttpStatusCode>();

        foreach (var origin in new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3", "203.0.113.4" })
        {
            statuses.Add(await StatusOfAsync(client, origin));
        }

        statuses.ShouldBe(
        [
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests
        ]);
    }

    [Fact]
    public async Task WithoutTheList_TheHeaderIsIgnoredAndVaryingItDoesNotEscapeTheQuota()
    {
        using var factory = new KestrelApiFactory(Settings());
        using var client = factory.CreateKestrelClient();

        var statuses = new List<HttpStatusCode>();

        foreach (var origin in new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3", "203.0.113.4" })
        {
            statuses.Add(await StatusOfAsync(client, origin));
        }

        statuses.ShouldBe(
        [
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests
        ]);
    }

    [Fact]
    public async Task WithTheProxyOnTheList_OnlyTheEntryTheProxyAppendedCounts()
    {
        using var factory = new KestrelApiFactory(Settings("127.0.0.0/8", "::1/128"));
        using var client = factory.CreateKestrelClient();

        var statuses = new List<HttpStatusCode>();

        foreach (var spoofed in new[] { "198.18.0.1", "198.18.0.2", "198.18.0.3" })
        {
            statuses.Add(await StatusOfAsync(client, $"{spoofed}, 198.51.100.7"));
        }

        statuses.ShouldBe(
        [
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests
        ]);
    }

    [Fact]
    public async Task WithTheProxyOnTheList_TheForwardedProtocolIsHonored()
    {
        using var factory = new KestrelApiFactory(Settings("127.0.0.0/8", "::1/128"), MapScheme);
        using var client = factory.CreateKestrelClient();
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, SchemeRoute);
        forwarded.Headers.Add("X-Forwarded-Proto", "https");
        using var plain = new HttpRequestMessage(HttpMethod.Get, SchemeRoute);

        using var behindProxy = await client.SendAsync(forwarded, CancellationToken.None);
        using var direct = await client.SendAsync(plain, CancellationToken.None);

        (await behindProxy.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("https");
        (await direct.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("http");
    }

    [Fact]
    public async Task WithoutTheList_TheForwardedProtocolIsIgnored()
    {
        using var factory = new KestrelApiFactory(Settings(), MapScheme);
        using var client = factory.CreateKestrelClient();
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, SchemeRoute);
        forwarded.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(forwarded, CancellationToken.None);

        (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("http");
    }

    [Fact]
    public async Task AnAuthenticatedCaller_IsKeyedByItsClientIdWhateverTheHeaderSays()
    {
        using var factory = new KestrelApiFactory(Settings("127.0.0.0/8", "::1/128"));
        using var client = factory.CreateKestrelClient();
        var token = TokenForge.Hmac(clientId: "pix-gateway");

        var statuses = new List<HttpStatusCode>();

        foreach (var origin in new[] { "203.0.113.1", "203.0.113.2", "203.0.113.3" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Read);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Forwarded-For", origin);
            using var response = await client.SendAsync(request, CancellationToken.None);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public void TheMiddleware_IsRegisteredOnlyWhenTheListIsNotEmpty()
    {
        using var withList = TestApiFactory.With(Settings("127.0.0.0/8"));
        using var withoutList = TestApiFactory.With(Settings());

        ForwardedHeadersSetup.IsEnabled(withList.Services).ShouldBeTrue();
        ForwardedHeadersSetup.IsEnabled(withoutList.Services).ShouldBeFalse();
    }

    [Fact]
    public void TheForwardingOptions_LimitTheChainToOneHopAndTrustOnlyTheListedNetworks()
    {
        using var factory = TestApiFactory.With(Settings("10.0.0.0/8", "192.168.4.0/24"));

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardLimit.ShouldBe(1);
        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.KnownProxies.ShouldBeEmpty();
        options.KnownIPNetworks.Select(network => network.ToString()).ShouldBe(["10.0.0.0/8", "192.168.4.0/24"]);
    }

    [Theory]
    [InlineData("not-a-network")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void ABadEntry_StopsTheProcessFromStarting(string entry)
    {
        using var factory = TestApiFactory.With(Settings(entry));

        var failure = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        failure.Message.ShouldContain("Security:ForwardedHeaders:KnownNetworks:0");
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/12")]
    [InlineData("203.0.113.7/32")]
    [InlineData("fd00::/8")]
    [InlineData("::1/128")]
    public void AWellFormedEntry_IsAccepted(string entry)
    {
        var validator = new ForwardedHeadersSettingsValidator();

        validator.Validate(null, new ForwardedHeadersSettings { KnownNetworks = [entry] }).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void WithoutAnyEntry_TheSettingsAreValidAndEmpty()
    {
        var validator = new ForwardedHeadersSettingsValidator();

        validator.Validate(null, new ForwardedHeadersSettings()).Succeeded.ShouldBeTrue();
        new ForwardedHeadersSettings().KnownNetworks.ShouldBeEmpty();
    }

    private static void MapScheme(IEndpointRouteBuilder routes)
    {
        routes.MapGet(SchemeRoute, (HttpContext context) => Results.Text(context.Request.Scheme)).AllowAnonymous();
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Read);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request, CancellationToken.None);

        return response.StatusCode;
    }

    private static Dictionary<string, string?> Settings(params string[] networks)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:ReadPerClient:Capacity"] = AnonymousQuota.ToString(CultureInfo.InvariantCulture),
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        };

        for (var index = 0; index < networks.Length; index++)
        {
            settings[$"Security:ForwardedHeaders:KnownNetworks:{index}"] = networks[index];
        }

        return settings;
    }
}
