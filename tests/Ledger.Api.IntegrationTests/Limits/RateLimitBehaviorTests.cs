using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Api.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Limits;

[Trait("Category", "Integration")]
public sealed class RateLimitBehaviorTests
{
    private static readonly string[] PolicyTag = ["policy"];


    private const string AccountRoute = "/__test/accounts/{accountId}/write";
    private const string Account = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";
    private const string OtherAccount = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d34";
    private const string ThirdAccount = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d35";

    private static Dictionary<string, string?> Limits(
        int writeClient = 1000,
        int readClient = 1000,
        int writeAccount = 1000,
        int writeConcurrency = 16)
    {
        return new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:WritePerClient:Capacity"] = writeClient.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:WritePerClient:RefillPerSecond"] = "1",
            ["RateLimiting:ReadPerClient:Capacity"] = readClient.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1",
            ["RateLimiting:WritePerAccount:Capacity"] = writeAccount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:WritePerAccount:RefillPerSecond"] = "1",
            ["RateLimiting:WriteConcurrency"] = writeConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Postgres:Sources:Write:MinPoolSize"] = "0",
            ["Postgres:Sources:Write:MaxPoolSize"] = writeConcurrency.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    [Fact]
    public async Task TheCallerQuota_RefusesTheRequestAfterTheCapacityWith429AndRetryAfterInAProblem()
    {
        using var factory = TestApiFactory.With(Limits(readClient: 3));
        using var client = factory.ClientWith(TokenForge.Hmac());

        for (var call = 0; call < 3; call++)
        {
            using var accepted = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var refused = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        refused.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");

        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(CancellationToken.None));

        problem.RootElement.GetProperty("code").GetString().ShouldBe("RATE_LIMITED");
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(429);
        problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        refused.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task TheCallerQuota_DoesNotMixTwoClients()
    {
        using var factory = TestApiFactory.With(Limits(readClient: 1));
        using var first = factory.ClientWith(TokenForge.Hmac(clientId: "pix-gateway"));
        using var second = factory.ClientWith(TokenForge.Hmac(clientId: "cards-core"));

        using var firstAccepted = await first.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var firstRefused = await first.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var secondAccepted = await second.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        firstAccepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        firstRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        secondAccepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheReadQuotaAndTheWriteQuota_AreIndependent()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1, readClient: 1));
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var write = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var read = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var secondWrite = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var secondRead = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        write.StatusCode.ShouldBe(HttpStatusCode.OK);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondWrite.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        secondRead.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheHealthRoutes_AreOutsideEveryLimit()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1, readClient: 1));
        using var client = factory.CreateClient();

        for (var call = 0; call < 20; call++)
        {
            using var live = await client.GetAsync("/health/live", CancellationToken.None);
            using var ready = await client.GetAsync("/health/ready", CancellationToken.None);

            live.StatusCode.ShouldBe(HttpStatusCode.OK);
            ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }
    }

    [Fact]
    public async Task ARouteWithoutAClass_IsNotLimited()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1, readClient: 1));
        using var client = factory.CreateClient();

        for (var call = 0; call < 10; call++)
        {
            using var response = await client.GetAsync(TestEndpointsStartupFilter.Open, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task WithTheLimiterOff_NothingIsRefused()
    {
        var settings = Limits(readClient: 1);
        settings["RateLimiting:Enabled"] = "false";
        using var factory = TestApiFactory.With(settings);
        using var client = factory.ClientWith(TokenForge.Hmac());

        for (var call = 0; call < 10; call++)
        {
            using var response = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TheRetryAfter_IsTheTimeUntilTheNextReplenishment()
    {
        var settings = Limits(readClient: 1);
        settings["RateLimiting:ReplenishmentSeconds"] = "30";
        using var factory = TestApiFactory.With(settings);
        using var client = factory.ClientWith(TokenForge.Hmac());
        using var accepted = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        using var refused = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ARequestWithoutToken_IsLimitedAsAnonymousBeforeItIsAuthorized()
    {
        using var factory = TestApiFactory.With(Limits(readClient: 1));
        using var client = factory.CreateClient();

        using var first = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var second = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        first.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ARefusalByQuota_HappensBeforeTheAuthorization()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1));
        using var client = factory.ClientWith(TokenForge.Hmac(scope: "ledger.read"));

        using var first = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var second = await PostAsync(client, TestEndpointsStartupFilter.Write);

        first.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheAccountQuota_RefusesTheFourthWriteOnTheSameAccountAndLeavesAnotherAccountAlone()
    {
        using var factory = TestApiFactory.With(Limits(writeAccount: 3), routes: MapAccountRoute);
        using var client = factory.ClientWith(TokenForge.Hmac());

        for (var call = 0; call < 3; call++)
        {
            using var accepted = await PostAsync(client, AccountPath(Account));

            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var refused = await PostAsync(client, AccountPath(Account));
        using var other = await PostAsync(client, AccountPath(OtherAccount));

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAccountIdThatIsNotAGuid_DoesNotCreateAnAccountLimiter()
    {
        using var factory = TestApiFactory.With(Limits(writeAccount: 1), routes: MapAccountRoute);
        using var client = factory.ClientWith(TokenForge.Hmac());

        for (var call = 0; call < 5; call++)
        {
            using var response = await PostAsync(client, AccountPath($"random-{call}"));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task TheCallerToken_IsSpentBeforeTheAccountQuotaAndIsNotGivenBackWhenTheAccountRefuses()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 3, writeAccount: 1), routes: MapAccountRoute);
        using var client = factory.ClientWith(TokenForge.Hmac());

        var statuses = new List<HttpStatusCode>();

        foreach (var account in new[] { Account, Account, OtherAccount, ThirdAccount })
        {
            using var response = await PostAsync(client, AccountPath(account));

            statuses.Add(response.StatusCode);
        }


        statuses.ShouldBe(
        [
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests
        ]);
    }

    [Fact]
    public async Task TheAccountToken_IsNotSpentWhenTheCallerQuotaRefusesFirst()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1, writeAccount: 2), routes: MapAccountRoute);
        using var pix = factory.ClientWith(TokenForge.Hmac(clientId: "pix-gateway"));
        using var cards = factory.ClientWith(TokenForge.Hmac(clientId: "cards-core"));
        using var operations = factory.ClientWith(TokenForge.Hmac(clientId: "ledger-ops"));

        using var first = await PostAsync(pix, AccountPath(Account));
        using var refusedByCaller = await PostAsync(pix, AccountPath(Account));
        using var second = await PostAsync(cards, AccountPath(Account));
        using var refusedByAccount = await PostAsync(operations, AccountPath(Account));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusedByCaller.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusedByAccount.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheConcurrencyLimit_RefusesTheExtraRequestWith503AndRetryAfterOneWithoutSpendingTheQuota()
    {
        using var gate = new SemaphoreSlim(0);
        using var entered = new SemaphoreSlim(0);
        using var factory = TestApiFactory.With(
            Limits(writeClient: 2, writeConcurrency: 1),
            routes: routes => HeldRequests.Map(routes, entered, gate));
        using var client = factory.ClientWith(TokenForge.Hmac());

        await HeldRequests.WhileHeldAsync(client, entered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(client);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            refused.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(1));
            using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(CancellationToken.None));

            problem.RootElement.GetProperty("code").GetString().ShouldBe("SERVICE_UNAVAILABLE");
            problem.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        });

        using var afterRelease = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var spent = await PostAsync(client, TestEndpointsStartupFilter.Write);

        afterRelease.StatusCode.ShouldBe(HttpStatusCode.OK);
        spent.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task TheConcurrencyPermit_ComesBackWhenTheCallerQuotaRefusesTheRequest()
    {
        using var factory = TestApiFactory.With(Limits(writeClient: 1, writeConcurrency: 1));
        using var client = factory.ClientWith(TokenForge.Hmac());

        using var first = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var refusedByQuota = await PostAsync(client, TestEndpointsStartupFilter.Write);
        using var other = factory.ClientWith(TokenForge.Hmac(clientId: "cards-core"));
        using var served = await PostAsync(other, TestEndpointsStartupFilter.Write);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        refusedByQuota.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheReadConcurrency_DoesNotShareThePermitsOfTheWriteClass()
    {
        using var gate = new SemaphoreSlim(0);
        using var entered = new SemaphoreSlim(0);
        using var factory = TestApiFactory.With(
            Limits(writeConcurrency: 1),
            routes: routes => HeldRequests.Map(routes, entered, gate));
        using var client = factory.ClientWith(TokenForge.Hmac());

        await HeldRequests.WhileHeldAsync(client, entered, gate, async () =>
        {
            using var read = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

            read.StatusCode.ShouldBe(HttpStatusCode.OK);
        });
    }

    [Fact]
    public async Task EveryPolicy_CountsItsRefusalOnceWithItsOwnLabel()
    {
        using var rejections = new MeterCapture("ledger.rate_limit.rejections");
        using var gate = new SemaphoreSlim(0);
        using var entered = new SemaphoreSlim(0);
        using var factory = TestApiFactory.With(
            Limits(writeClient: 100, readClient: 1, writeAccount: 1, writeConcurrency: 1),
            routes: routes =>
            {
                MapAccountRoute(routes);
                HeldRequests.Map(routes, entered, gate);
            });
        using var client = factory.ClientWith(TokenForge.Hmac());
        using var read = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var readRefused = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var account = await PostAsync(client, AccountPath(Account));
        using var accountRefused = await PostAsync(client, AccountPath(Account));

        await HeldRequests.WhileHeldAsync(client, entered, gate, async () =>
        {
            using var concurrencyRefused = await HeldRequests.PostAsync(client);

            concurrencyRefused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });

        readRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        accountRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        PoliciesCounted(rejections).Order(StringComparer.Ordinal).ToList()
            .ShouldBe(["read-per-client", "write-concurrency", "write-per-account"]);
        rejections.Measurements.ShouldAllBe(measurement => measurement.Tags.Keys.SequenceEqual(PolicyTag));
    }

    [Fact]
    public async Task EachOfTheSixPolicies_CountsExactlyOneRefusalWithItsOwnLabel()
    {
        using var rejections = new MeterCapture("ledger.rate_limit.rejections");
        using var gate = new SemaphoreSlim(0);
        using var writeEntered = new SemaphoreSlim(0);
        using var balanceEntered = new SemaphoreSlim(0);
        using var statementEntered = new SemaphoreSlim(0);
        var settings = Limits(writeClient: 1, readClient: 1, writeAccount: 1, writeConcurrency: 1);
        settings["RateLimiting:BalanceConcurrency"] = "1";
        settings["RateLimiting:StatementConcurrency"] = "1";
        settings["Postgres:Sources:Balance:MinPoolSize"] = "0";
        settings["Postgres:Sources:Balance:MaxPoolSize"] = "1";
        settings["Postgres:Sources:Statement:MinPoolSize"] = "0";
        settings["Postgres:Sources:Statement:MaxPoolSize"] = "1";
        using var factory = TestApiFactory.With(
            settings,
            routes: routes =>
            {
                MapAccountRoute(routes);
                HeldRequests.Map(routes, writeEntered, gate);
                HeldRequests.Map(routes, balanceEntered, gate, requestClass: RequestClass.Balance, route: HeldRequests.BalanceRoute);
                HeldRequests.Map(routes, statementEntered, gate, requestClass: RequestClass.Statement, route: HeldRequests.StatementRoute);
            });
        using var writer = factory.ClientWith(TokenForge.Hmac(clientId: "writer"));
        using var reader = factory.ClientWith(TokenForge.Hmac(clientId: "reader"));
        using var firstOnAccount = factory.ClientWith(TokenForge.Hmac(clientId: "first-on-account"));
        using var secondOnAccount = factory.ClientWith(TokenForge.Hmac(clientId: "second-on-account"));
        using var holdWrite = factory.ClientWith(TokenForge.Hmac(clientId: "hold-write"));
        using var refusedWrite = factory.ClientWith(TokenForge.Hmac(clientId: "refused-write"));
        using var holdBalance = factory.ClientWith(TokenForge.Hmac(clientId: "hold-balance"));
        using var refusedBalance = factory.ClientWith(TokenForge.Hmac(clientId: "refused-balance"));
        using var holdStatement = factory.ClientWith(TokenForge.Hmac(clientId: "hold-statement"));
        using var refusedStatement = factory.ClientWith(TokenForge.Hmac(clientId: "refused-statement"));

        using var writeAccepted = await PostAsync(writer, TestEndpointsStartupFilter.Write);
        using var writeRefused = await PostAsync(writer, TestEndpointsStartupFilter.Write);
        using var readAccepted = await reader.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var readRefused = await reader.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);
        using var accountAccepted = await PostAsync(firstOnAccount, AccountPath(Account));
        using var accountRefused = await PostAsync(secondOnAccount, AccountPath(Account));

        await HeldRequests.WhileHeldAsync(holdWrite, writeEntered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(refusedWrite);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });

        await HeldRequests.WhileHeldAsync(holdBalance, balanceEntered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(refusedBalance, HeldRequests.BalanceRoute);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }, HeldRequests.BalanceRoute);

        await HeldRequests.WhileHeldAsync(holdStatement, statementEntered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(refusedStatement, HeldRequests.StatementRoute);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }, HeldRequests.StatementRoute);

        writeRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        readRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        accountRefused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        PoliciesCounted(rejections).Order(StringComparer.Ordinal).ToList().ShouldBe(
        [
            "balance-concurrency",
            "read-per-client",
            "statement-concurrency",
            "write-concurrency",
            "write-per-account",
            "write-per-client"
        ]);
    }

    [Fact]
    public async Task TheQuotaRefusal_LogsTheRateLimitEventWithPolicyClientAndRetryAfter()
    {
        var settings = Limits(readClient: 1);
        settings["RateLimiting:ReplenishmentSeconds"] = "30";
        using var factory = TestApiFactory.With(settings);
        using var client = factory.ClientWith(TokenForge.Hmac(clientId: "pix-gateway"));
        using var accepted = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        using var refused = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        var logged = factory.Sink.Events
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("Rate limit exceeded", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        logged.Level.ShouldBe(LogEventLevel.Information);
        Property(logged, "Policy").ShouldBe("read-per-client");
        Property(logged, "ClientId").ShouldBe("pix-gateway");
        Property(logged, "RetryAfterSeconds").ShouldBe("30");
    }

    [Fact]
    public async Task TheConcurrencyRefusal_LogsAWarningWithPolicyAndClient()
    {
        using var gate = new SemaphoreSlim(0);
        using var entered = new SemaphoreSlim(0);
        using var factory = TestApiFactory.With(
            Limits(writeConcurrency: 1),
            routes: routes => HeldRequests.Map(routes, entered, gate));
        using var client = factory.ClientWith(TokenForge.Hmac(clientId: "pix-gateway"));

        await HeldRequests.WhileHeldAsync(client, entered, gate, async () =>
        {
            using var refused = await HeldRequests.PostAsync(client);

            refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        });

        var logged = factory.Sink.Events
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("Concurrency limit reached", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        logged.Level.ShouldBe(LogEventLevel.Warning);
        Property(logged, "Policy").ShouldBe("write-concurrency");
        Property(logged, "ClientId").ShouldBe("pix-gateway");
    }

    [Fact]
    public async Task AnAnonymousRefusal_LogsANullClient()
    {
        using var factory = TestApiFactory.With(Limits(readClient: 1));
        using var client = factory.CreateClient();
        using var first = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        using var second = await client.GetAsync(TestEndpointsStartupFilter.Read, CancellationToken.None);

        var logged = factory.Sink.Events
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("Rate limit exceeded", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        Property(logged, "ClientId").ShouldBe("null");
    }

    private static void MapAccountRoute(IEndpointRouteBuilder routes)
    {
        routes.MapPost(AccountRoute, (string accountId) => Results.Ok(new { accountId }))
            .RequireAuthorization("ledger.write")
            .WithRequestClass(RequestClass.Write);
    }

    private static string AccountPath(string accountId) => $"/__test/accounts/{accountId}/write";

    private static List<string> PoliciesCounted(MeterCapture capture)
    {
        return [.. capture.Measurements.Select(measurement => (string)measurement.Tags["policy"]!)];
    }

    private static string Property(LogEvent logEvent, string name) => CapturingLogSinkProperty(logEvent, name);

    private static string CapturingLogSinkProperty(LogEvent logEvent, string name)
    {
        return Observability.CapturingLogSink.Property(logEvent, name).Trim('"');
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }
}
