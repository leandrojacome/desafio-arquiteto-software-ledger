using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Health;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ApiReadinessWithDatabaseTests(PostgresFixture postgres)
{
    private static readonly TimeSpan ReadinessBudget = TimeSpan.FromSeconds(3);

    [DockerFact]
    public async Task Ready_WithTheDatabaseUpAndTheBrokerUnreachable_IsHealthy()
    {
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(response)).ShouldBe("Healthy");
    }

    [DockerFact]
    public async Task Ready_WhileTheWritePoolIsExhausted_StaysHealthyBecauseItProbesTheStatementPool()
    {
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
        {
            ["Postgres:Sources:Write:MaxPoolSize"] = "2",
            ["Postgres:Sources:Write:MinPoolSize"] = "0",
            ["RateLimiting:WriteConcurrency"] = "2"
        }));
        using var client = factory.CreateClient();
        var connections = factory.Services.GetRequiredService<IPostgresConnectionFactory>();
        await using var first = await connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
        await using var second = await connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);

        await Should.ThrowAsync<Exception>(async () =>
        {
            await using var third = await connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
        });

        var watch = Stopwatch.StartNew();
        using var response = await client.GetAsync("/health/ready", CancellationToken.None);

        watch.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(response)).ShouldBe("Healthy");
        watch.Elapsed.ShouldBeLessThan(ReadinessBudget);
    }

    [DockerFact]
    public async Task Ready_IsAnsweredFromTheCacheForAFewSecondsAndTheNextProbeSeesTheOutage()
    {
        var endpoint = new NpgsqlConnectionStringBuilder(postgres.AdministrativeSource.ConnectionString);
        await using var proxy = TcpProxy.Start(endpoint.Host ?? "127.0.0.1", endpoint.Port);
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
        {
            ["Postgres:Host"] = "127.0.0.1",
            ["Postgres:Port"] = proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Resilience:Health:CacheSeconds"] = "2"
        }));
        using var client = factory.CreateClient();

        using var up = await client.GetAsync("/health/ready", CancellationToken.None);

        proxy.Pause();

        using var cached = await client.GetAsync("/health/ready", CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(2.5), CancellationToken.None);
        using var afterTheWindow = await client.GetAsync("/health/ready", CancellationToken.None);

        up.StatusCode.ShouldBe(HttpStatusCode.OK);
        cached.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterTheWindow.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        afterTheWindow.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [DockerFact]
    public async Task Ready_WhenTheKeyFolderIsEmpty_IsDegradedButStillServing()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"ledger-empty-keys-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(empty);

        try
        {
            using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
            {
                ["Security:Pii:Provider"] = "Directory",
                ["Security:Pii:Directory"] = empty
            }));
            using var client = factory.CreateClient();

            using var response = await client.GetAsync("/health/ready", CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await StatusOf(response)).ShouldBe("Degraded");
            response.Headers.Contains("Retry-After").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [DockerFact]
    public async Task Ready_AfterTheKeyDirectoryDisappearsFollowingTheFirstSnapshot_StaysHealthy()
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
        {
            ["Security:Pii:Provider"] = "Directory",
            ["Security:Pii:Directory"] = keys.Root
        }));
        using var client = factory.CreateClient();

        using var before = await client.GetAsync("/health/ready", CancellationToken.None);

        Directory.Delete(keys.Root, recursive: true);

        using var after = await client.GetAsync("/health/ready", CancellationToken.None);

        (await StatusOf(before)).ShouldBe("Healthy");
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StatusOf(after)).ShouldBe("Healthy");
    }

    [DockerFact]
    public async Task AnEmptyKeyFolder_MakesAccountCreationAnswer503WhileTheRestOfTheLedgerServes()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"ledger-empty-keys-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(empty);

        try
        {
            using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
            {
                ["Security:Pii:Provider"] = "Directory",
                ["Security:Pii:Directory"] = empty,
                ["Authorization:AccountProvisioningClients:0"] = "*"
            }));
            using var client = factory.ClientWith(TokenForge.Hmac());
            using var creation = new StringContent("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BRL\"}");
            creation.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            using var created = await client.PostAsync("/v1/accounts", creation, CancellationToken.None);
            using var balance = await client.GetAsync(
                "/v1/accounts/0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33/balance",
                CancellationToken.None);

            created.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            created.Headers.RetryAfter.ShouldNotBeNull();
            balance.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void AMalformedKey_StopsTheProcessFromStarting()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ledger-bad-keys-{Guid.CreateVersion7():N}");
        var version = Directory.CreateDirectory(Path.Combine(directory, "1"));
        File.WriteAllText(Path.Combine(version.FullName, "encryption.key"), "this-is-not-base64-of-32-bytes");
        File.WriteAllText(Path.Combine(version.FullName, "blind-index.key"), "neither-is-this-one");

        try
        {
            using var factory = TestApiFactory.With(new Dictionary<string, string?>
            {
                ["Security:Pii:Provider"] = "Directory",
                ["Security:Pii:Directory"] = directory
            });

            var failure = Should.Throw<Exception>(() => factory.CreateClient());
            var message = Describe(failure);

            message.ShouldContain("encryption.key");
            message.ShouldContain("must be a base64 text");
            Chain(failure).ShouldContain(exception => exception is KeyMaterialRejectedException);
            message.ShouldNotContain("this-is-not-base64-of-32-bytes");
            message.ShouldNotContain("neither-is-this-one");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static List<Exception> Chain(Exception exception)
    {
        var chain = new List<Exception>();

        for (var current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        return chain;
    }

    private static string Describe(Exception exception) => string.Join(" | ", Chain(exception).Select(item => item.Message));

    [DockerFact]
    public async Task Ready_WhenTheSchemaIsBehindTheCode_Is503UntilTheMigrationRuns()
    {
        await using var database = await postgres.CreateEmptyDatabaseAsync(CancellationToken.None);
        using var factory = TestApiFactory.With(postgres.ConfigurationWith(new Dictionary<string, string?>
        {
            ["Postgres:Database"] = database.Name,
            ["Resilience:Health:CacheSeconds"] = "1"
        }));
        using var client = factory.CreateClient();

        using var behind = await client.GetAsync("/health/ready", CancellationToken.None);
        using var live = await client.GetAsync("/health/live", CancellationToken.None);

        behind.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        behind.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(5));
        (await StatusOf(behind)).ShouldBe("Unhealthy");
        live.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await database.MigrateAsync(CancellationToken.None);

        report.Succeeded.ShouldBeTrue();

        await ConditionWait.UntilAsync(
            async () =>
            {
                using var probe = await client.GetAsync("/health/ready", CancellationToken.None);

                return probe.StatusCode == HttpStatusCode.OK;
            },
            TimeSpan.FromSeconds(20),
            "the readiness to go back to 200 once the schema is current",
            TimeSpan.FromMilliseconds(500));
    }

    private static async Task<string?> StatusOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("status").GetString();
    }
}
