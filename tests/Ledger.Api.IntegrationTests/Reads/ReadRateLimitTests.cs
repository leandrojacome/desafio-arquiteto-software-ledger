using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
public sealed class ReadRateLimitTests : IDisposable
{
    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    private readonly ReadApiFactory _factory = ReadApiFactory.WithoutDatabase(new Dictionary<string, string?>
    {
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:ReplenishmentSeconds"] = "3600",
        ["RateLimiting:ReadPerClient:Capacity"] = "2",
        ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
    });

    [Theory]
    [InlineData("balance")]
    [InlineData("entries")]
    public async Task Reads_BeyondTheQuotaOfTheCaller_Return429RateLimitedWithRetryAfter(string route)
    {
        using var client = ReadApiClient.For(_factory, TestTokenFactory.Create("ledger.read", $"quota-{route}"));
        var path = Invalid(route);
        var statuses = new List<HttpStatusCode>();
        ReadResponse? rejected = null;

        for (var attempt = 0; attempt < 6 && rejected is null; attempt++)
        {
            var response = await client.GetAsync(path);

            statuses.Add(response.Status);

            if (response.Status == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
            else
            {
                response.Dispose();
            }
        }

        statuses.Take(2).ShouldAllBe(status => status == HttpStatusCode.BadRequest);
        rejected.ShouldNotBeNull();

        using (rejected)
        {
            rejected.ShouldBeProblem(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Limite de requisições excedido");
            int.Parse(rejected.Header("Retry-After") ?? "0", System.Globalization.CultureInfo.InvariantCulture)
                .ShouldBeGreaterThanOrEqualTo(1);
        }
    }

    [Fact]
    public async Task Reads_OfAnotherCaller_AreNotAffectedByTheQuotaOfTheFirst()
    {
        using var heavy = ReadApiClient.For(_factory, TestTokenFactory.Create("ledger.read", "heavy-reader"));
        using var light = ReadApiClient.For(_factory, TestTokenFactory.Create("ledger.read", "light-reader"));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await heavy.GetAsync(Invalid("balance"))).Dispose();
        }

        using var response = await light.GetAsync(Invalid("balance"));

        response.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reads_DoNotSpendTheWriteQuotaOfTheSameCaller()
    {
        var token = TestTokenFactory.Create("ledger.read ledger.write", "mixed-caller");
        using var client = ReadApiClient.For(_factory, token);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            (await client.GetAsync(Invalid("entries"))).Dispose();
        }

        using var write = await client.SendAsync(HttpMethod.Post, $"/v1/accounts/{Account}/entries");

        write.Status.ShouldNotBe(HttpStatusCode.TooManyRequests, write.Body);
    }

    public void Dispose() => _factory.Dispose();

    private static string Invalid(string route) =>
        route == "balance"
            ? $"{ReadApiClient.BalancePath(Account)}?asOf=abc"
            : $"{ReadApiClient.StatementPath(Account)}?limit=0";
}
