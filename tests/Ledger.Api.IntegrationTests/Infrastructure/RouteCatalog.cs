using Ledger.Api.RateLimiting;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed record BusinessRoute(
    HttpMethod Method,
    string Template,
    string Path,
    string Policy,
    string Scope,
    RequestClass Class)
{
    public bool IsWrite => Scope == "ledger.write";
}

internal static class RouteCatalog
{
    public const string UnknownAccount = "not-an-account";

    public static IReadOnlyList<BusinessRoute> BusinessRoutes { get; } =
    [
        new(HttpMethod.Post, "/v1/accounts", "/v1/accounts", "AccountProvisioning", "ledger.write", RequestClass.Write),
        new(
            HttpMethod.Post,
            "/v1/accounts/{accountId}/entries",
            $"/v1/accounts/{UnknownAccount}/entries",
            "ledger.write",
            "ledger.write",
            RequestClass.Write),
        new(
            HttpMethod.Post,
            "/v1/accounts/{accountId}/entries/{entryId}/reversals",
            $"/v1/accounts/{UnknownAccount}/entries/{UnknownAccount}/reversals",
            "ledger.write",
            "ledger.write",
            RequestClass.Write),
        new(
            HttpMethod.Get,
            "/v1/accounts/{accountId}/balance",
            $"/v1/accounts/{UnknownAccount}/balance",
            "ledger.read",
            "ledger.read",
            RequestClass.Balance),
        new(
            HttpMethod.Get,
            "/v1/accounts/{accountId}/entries",
            $"/v1/accounts/{UnknownAccount}/entries",
            "ledger.read",
            "ledger.read",
            RequestClass.Statement)
    ];
}
