using System.Net;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class ComposeSmokeE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task TheApiAndTheWorker_AnswerLiveAndReadyWithHealthyBodies()
    {
        using var api = stack.CreateClient();
        using var worker = stack.Stack.CreateWorkerClient();

        foreach (var (client, name) in new[] { (api, "api"), (worker, "worker") })
        {
            foreach (var path in new[] { "/health/live", "/health/ready" })
            {
                using var response = await client.GetAsync(path, CancellationToken.None);
                var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

                ((int)response.StatusCode).ShouldBe(200, $"{name} {path}: {body}");
                body.ShouldContain("Healthy");
                response.Headers.CacheControl.ShouldNotBeNull($"{name} {path}: Cache-Control is missing").NoStore.ShouldBeTrue();
            }
        }
    }

    [E2EFact]
    public async Task TheMigrator_FinishedWithExitCodeZero_AndTheJournalHasOneRowPerScript()
    {
        var exitCode = await stack.Stack.Compose.ExitCodeOfAsync(ComposeControl.MigratorService);
        var scripts = Directory.GetFiles(E2EPaths.MigrationsDirectory(), "*.sql").Length;
        var journal = await stack.Stack.Database().SchemaVersionCountAsync();

        exitCode.ShouldBe(0);
        journal.ShouldBe(scripts);
    }

    [E2EFact]
    public async Task ACorrelationIdFromTheCaller_ComesBackAndAnInvalidOneIsReplaced()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        const string Supplied = "e2e-correlation-0001";

        var kept = await api.SendAsync(
            HttpMethod.Get,
            "/health/live",
            anonymous: true,
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = Supplied });
        var replaced = await api.SendAsync(
            HttpMethod.Get,
            "/health/live",
            anonymous: true,
            headers: new Dictionary<string, string> { ["X-Correlation-Id"] = "short" });
        var generated = await api.SendAsync(HttpMethod.Get, "/health/live", anonymous: true);

        kept.Header("X-Correlation-Id").ShouldBe(Supplied);
        replaced.Header("X-Correlation-Id").ShouldNotBe("short");
        replaced.Header("X-Correlation-Id").ShouldNotBeNullOrWhiteSpace();
        generated.Header("X-Correlation-Id").ShouldNotBeNullOrWhiteSpace();
    }
}

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class HealthE2ETests(E2EFixture stack)
{
    [E2EFact]
    public async Task Live_ReturnsOk()
    {
        using var client = stack.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [E2EFact]
    public async Task Ready_ReturnsOkWhenTheStackIsUp()
    {
        using var client = stack.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
