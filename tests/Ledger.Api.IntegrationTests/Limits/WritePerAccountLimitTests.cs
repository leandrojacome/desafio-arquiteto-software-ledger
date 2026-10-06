using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Limits;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class WritePerAccountLimitTests(PostgresFixture postgres)
{
    private const int AccountQuota = 3;

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task TheFourthEntryOnTheSameAccount_Gets429WithRetryAfterAndStoresNothing_WhileAnotherAccountIsServed()
    {
        await using var factory = new WriteApiFactory(postgres, QuotaSettings());
        var client = new WriteClient(factory.CreateClient());
        var busy = await CreateAccountAsync(client);
        var calm = await CreateAccountAsync(client);

        for (var entry = 0; entry < AccountQuota; entry++)
        {
            var accepted = await client.PostEntryAsync(busy, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey());

            accepted.StatusCode.ShouldBe(201, accepted.Body);
        }

        var refusedKey = WriteClient.NewKey();
        var refused = await client.PostEntryAsync(busy, WriteClient.EntryBody("CREDIT", "10.00"), refusedKey);
        var other = await client.PostEntryAsync(calm, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey());

        refused.StatusCode.ShouldBe(429, refused.Body);
        refused.ContentType.ShouldBe("application/problem+json");
        refused.Text("code").ShouldBe("RATE_LIMITED");
        int.Parse(refused.Header("Retry-After") ?? "0", System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThanOrEqualTo(1);
        other.StatusCode.ShouldBe(201, other.Body);
        (await _data.CountEntriesAsync(busy)).ShouldBe(AccountQuota);
        (await _data.CountKeysAsync(busy)).ShouldBe(AccountQuota);
    }

    [DockerFact]
    public async Task ARefusedEntry_CanBeRepeatedWithTheSameKeyOnceTheQuotaRefills()
    {
        var settings = QuotaSettings();
        settings["RateLimiting:WritePerAccount:Capacity"] = "1";
        settings["RateLimiting:ReplenishmentSeconds"] = "1";
        await using var factory = new WriteApiFactory(postgres, settings);
        var client = new WriteClient(factory.CreateClient());
        var account = await CreateAccountAsync(client);
        var key = WriteClient.NewKey();
        var first = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey());
        var attempts = new List<int>();

        await ConditionWait.UntilAsync(
            async () =>
            {
                var attempt = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), key);
                attempts.Add(attempt.StatusCode);

                return attempt.StatusCode != 429;
            },
            TimeSpan.FromSeconds(15),
            "the account quota to refill");

        first.StatusCode.ShouldBe(201, first.Body);
        attempts.Last().ShouldBe(201);
        attempts.Take(attempts.Count - 1).ShouldAllBe(status => status == 429);
        (await _data.CountEntriesAsync(account)).ShouldBe(2);
        (await _data.CountKeysAsync(account)).ShouldBe(2);
    }

    [DockerFact]
    public async Task RequestsThatAreNotAuthorized_NeverSpendTheQuotaOfTheAccount()
    {
        await using var factory = new WriteApiFactory(postgres, QuotaSettings());
        var client = new WriteClient(factory.CreateClient());
        var account = await CreateAccountAsync(client);
        var anonymous = new WriteRequestOptions { Anonymous = true };
        var readOnly = new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting") };
        var wrongScope = new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.writer", clientId: "pix-core") };
        var withoutClient = new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.write", clientId: null) };
        var oversizedClient = new WriteRequestOptions
        {
            Token = TestTokenFactory.Create(scope: "ledger.write", clientId: new string('c', 5000))
        };
        var forged = new WriteRequestOptions { Token = TestTokenFactory.Create(signingKey: "a-different-signing-key-with-more-than-32-chars") };

        var refusals = new List<int>();

        for (var attempt = 0; attempt < AccountQuota * 4; attempt++)
        {
            foreach (var options in new[] { anonymous, readOnly, wrongScope, withoutClient, oversizedClient, forged })
            {
                var refused = await client.PostEntryAsync(
                    account,
                    WriteClient.EntryBody("CREDIT", "10.00"),
                    WriteClient.NewKey(),
                    options);

                refusals.Add(refused.StatusCode);
            }
        }

        refusals.ShouldAllBe(status => status == 401 || status == 403);

        var accepted = new List<int>();

        for (var entry = 0; entry < AccountQuota + 1; entry++)
        {
            var response = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "10.00"), WriteClient.NewKey());
            accepted.Add(response.StatusCode);
        }

        accepted.ShouldBe([201, 201, 201, 429]);
        (await _data.CountEntriesAsync(account)).ShouldBe(AccountQuota);
    }

    [DockerFact]
    public async Task ATokenThatCannotWrite_DoesNotCreateAnAccountBucketOrTouchTheBucketOfAnotherCaller()
    {
        await using var factory = new WriteApiFactory(postgres, QuotaSettings());
        var client = new WriteClient(factory.CreateClient());
        var account = await CreateAccountAsync(client);
        var reporting = new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting") };

        for (var attempt = 0; attempt < AccountQuota * 3; attempt++)
        {
            var refused = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), reporting);

            refused.ShouldBeProblem(403, "FORBIDDEN");
        }

        var ledger = new WriteRequestOptions { Token = TestTokenFactory.Create(scope: "ledger.write", clientId: "pix-core") };
        var statuses = new List<int>();

        for (var entry = 0; entry < AccountQuota; entry++)
        {
            var response = await client.PostEntryAsync(account, WriteClient.EntryBody("CREDIT", "1.00"), WriteClient.NewKey(), ledger);
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldAllBe(status => status == 201);
    }

    [DockerFact]
    public async Task TheCallerQuotas_KeepReadsAndWritesApartAndDoNotMixTwoClients()
    {
        var settings = QuotaSettings();
        settings["RateLimiting:WritePerAccount:Capacity"] = "1000";
        settings["RateLimiting:WritePerClient:Capacity"] = "5";
        settings["RateLimiting:ReadPerClient:Capacity"] = "5";
        await using var factory = new WriteApiFactory(postgres, settings);
        var writer = new WriteClient(factory.CreateClient());
        var account = await CreateAccountAsync(writer, new WriteRequestOptions { Token = TokenForgeFor("writer-a") });
        var options = new WriteRequestOptions { Token = TokenForgeFor("writer-b") };

        var statuses = new List<int>();

        for (var call = 0; call < 7; call++)
        {
            var response = await writer.PostEntryAsync(
                account,
                WriteClient.EntryBody("CREDIT", "1.00"),
                WriteClient.NewKey(),
                options);

            statuses.Add(response.StatusCode);
        }

        var read = await writer.SendAsync(
            HttpMethod.Get,
            $"/v1/accounts/{account}/balance",
            options: options);

        statuses.Take(5).ShouldAllBe(status => status == 201);
        statuses.Skip(5).ShouldAllBe(status => status == 429);
        read.StatusCode.ShouldBe(200, read.Body);
    }

    private static string TokenForgeFor(string clientId) =>
        Security.TokenForge.Hmac(clientId: clientId);

    private static async Task<string> CreateAccountAsync(WriteClient client, WriteRequestOptions? options = null)
    {
        var response = await client.PostAccountAsync(options: options);

        response.StatusCode.ShouldBe(201, response.Body);

        return response.Text("accountId");
    }

    private static Dictionary<string, string?> QuotaSettings()
    {
        var settings = new Dictionary<string, string?>(WriteApiFactory.WidePool)
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:ReplenishmentSeconds"] = "3600",
            ["RateLimiting:WritePerAccount:Capacity"] = AccountQuota.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:WritePerAccount:RefillPerSecond"] = "1",
            ["RateLimiting:WritePerClient:Capacity"] = "1000",
            ["RateLimiting:WritePerClient:RefillPerSecond"] = "1",
            ["RateLimiting:ReadPerClient:Capacity"] = "1000",
            ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
        };

        return settings;
    }
}
