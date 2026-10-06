using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Health;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WorkerHealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Live_ReturnsOkWithoutTouchingTheDatabase()
    {
        await using var factory = new WorkerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        var cacheControl = response.Headers.CacheControl;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        cacheControl.ShouldNotBeNull();
        cacheControl.NoStore.ShouldBeTrue();
        (await StatusOf(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_WhenTheDatabaseIsUnreachable_ReturnsServiceUnavailableWithRetryAfter()
    {
        await using var factory = new WorkerFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        var retryAfter = response.Headers.RetryAfter;

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        retryAfter.ShouldNotBeNull();
        retryAfter.Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOf(response)).ShouldBe("Unhealthy");
    }

    [DockerFact]
    public async Task Ready_WhenTheDatabaseIsUpAndTheBrokerIsDown_IsDegradedButStillOk()
    {
        await using var factory = new DatabaseWorkerFactory(postgres);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(response)).ShouldBe("Degraded");
    }

    private static async Task<string?> StatusOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("status").GetString();
    }
}
