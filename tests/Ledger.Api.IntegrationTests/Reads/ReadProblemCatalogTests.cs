using System.Net;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Abstractions;
using Ledger.Application.Balances;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Reads;

[Trait("Category", "Integration")]
public sealed class ReadProblemCatalogTests
{
    private const string Sentinel = "SENTINEL-VALUE-5521";
    private const string RateLimited = "RATE_LIMITED";

    private static readonly AccountId Account = AccountId.From(Guid.CreateVersion7()).Value;

    [Theory]
    [InlineData("VALIDATION_FAILED", 400, "Falha na validação da requisição")]
    [InlineData("INVALID_AS_OF", 400, "Instante 'asOf' inválido")]
    [InlineData("UNAUTHENTICATED", 401, "Autenticação necessária")]
    [InlineData("FORBIDDEN", 403, "Acesso negado")]
    [InlineData("ACCOUNT_NOT_FOUND", 404, "Conta não encontrada")]
    [InlineData("METHOD_NOT_ALLOWED", 405, "Método não permitido")]
    [InlineData(RateLimited, 429, "Limite de requisições excedido")]
    [InlineData("INTERNAL_ERROR", 500, "Erro interno")]
    [InlineData("SERVICE_UNAVAILABLE", 503, "Serviço temporariamente indisponível")]
    public async Task EveryCodeOfTheReadCatalog_IsProducedWithTheCatalogShape(string code, int status, string title)
    {
        using var factory = FactoryFor(code);
        using var client = ClientFor(code, factory);
        using var response = await RunAsync(code, client);

        response.ShouldBeProblem((HttpStatusCode)status, code, title);
        response.ShouldCarryNoInternals();
        response.Body.ShouldNotContain("SENTINEL");
        response.Header("Cache-Control").ShouldNotBeNull().ShouldContain("no-store");
        response.HasHeader("ETag").ShouldBeFalse();
    }

    [Fact]
    public async Task AReaderThatThrowsATransientDatabaseFailure_BecomesA503WithRetryAfter()
    {
        using var factory = ReadApiFactory.WithoutDatabase(
            configureServices: services => Replace(
                services,
                new NpgsqlException($"{Sentinel} connection lost", new TimeoutException(Sentinel))));
        using var client = ReadApiClient.For(factory);

        using var response = await client.BalanceAsync(Account);

        response.ShouldBeProblem(HttpStatusCode.ServiceUnavailable, "SERVICE_UNAVAILABLE", "Serviço temporariamente indisponível");
        response.Header("Retry-After").ShouldBe("1");
        response.Body.ShouldNotContain("SENTINEL");
    }

    private static ReadApiFactory FactoryFor(string code)
    {
        return code switch
        {
            RateLimited => ReadApiFactory.WithoutDatabase(new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:ReplenishmentSeconds"] = "3600",
                ["RateLimiting:ReadPerClient:Capacity"] = "1",
                ["RateLimiting:ReadPerClient:RefillPerSecond"] = "1"
            }),
            "INTERNAL_ERROR" => ReadApiFactory.WithoutDatabase(
                configureServices: services => Replace(
                    services,
                    new InvalidOperationException($"{Sentinel} SELECT secret FROM accounts Host=db"))),
            _ => ReadApiFactory.WithoutDatabase()
        };
    }

    private static ReadApiClient ClientFor(string code, ReadApiFactory factory)
    {
        return code switch
        {
            "UNAUTHENTICATED" => ReadApiClient.Anonymous(factory),
            "FORBIDDEN" => ReadApiClient.For(factory, TestTokenFactory.Create("ledger.write")),
            RateLimited => ReadApiClient.For(factory, TestTokenFactory.Create("ledger.read", "catalog-quota")),
            _ => ReadApiClient.For(factory)
        };
    }

    private static async Task<ReadResponse> RunAsync(string code, ReadApiClient client)
    {
        switch (code)
        {
            case "VALIDATION_FAILED":
                return await client.StatementAsync(Account, $"limit=0&foo={Sentinel}");
            case "INVALID_AS_OF":
                return await client.BalanceAsOfAsync(Account, Sentinel);
            case "UNAUTHENTICATED":
                return await client.GetAsync($"{ReadApiClient.BalancePath(Account)}?asOf={Sentinel}");
            case "ACCOUNT_NOT_FOUND":
                return await client.GetAsync($"/v1/accounts/not-a-guid/balance?asOf={Sentinel}");
            case "METHOD_NOT_ALLOWED":
                return await client.SendAsync(HttpMethod.Put, ReadApiClient.BalancePath(Account));
            case RateLimited:
                return await UntilRateLimitedAsync(client);
            default:
                return await client.BalanceAsync(Account);
        }
    }

    private static async Task<ReadResponse> UntilRateLimitedAsync(ReadApiClient client)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.StatementAsync(Account, "limit=0");

            if (response.Status == HttpStatusCode.TooManyRequests)
            {
                return response;
            }

            response.Dispose();
        }

        throw new InvalidOperationException("The rate limit was never reached.");
    }

    private static void Replace(IServiceCollection services, Exception failure)
    {
        services.RemoveAll<IBalanceReader>();
        services.AddSingleton<IBalanceReader>(new ThrowingBalanceReader(failure));
    }

    private sealed class ThrowingBalanceReader(Exception failure) : IBalanceReader
    {
        public Task<Result<CurrentBalanceReading>> ReadCurrentAsync(AccountId accountId, CancellationToken cancellationToken) =>
            throw failure;

        public Task<Result<BalanceAtReading>> ReadAtAsync(
            AccountId accountId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken) => throw failure;
    }
}
