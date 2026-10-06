using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Health;

[Trait("Category", "Integration")]
public sealed class HealthEndpointsTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    [Fact]
    public async Task Live_ReturnsOkWithoutTouchingTheDatabase()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Live_DoesNotRequireAuthenticationAndForbidsCaching()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        var cacheControl = response.Headers.CacheControl;

        cacheControl.ShouldNotBeNull();
        cacheControl.NoStore.ShouldBeTrue();
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task Ready_WhenTheDatabaseIsUnreachable_ReturnsServiceUnavailableWithRetryAfter()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        var retryAfter = response.Headers.RetryAfter;

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        retryAfter.ShouldNotBeNull();
        retryAfter.Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOf(response)).ShouldBe("Unhealthy");
    }

    private static async Task<string?> StatusOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("status").GetString();
    }
}
