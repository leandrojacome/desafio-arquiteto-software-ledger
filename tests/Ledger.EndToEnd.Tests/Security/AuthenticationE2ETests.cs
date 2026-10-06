using System.Security.Cryptography;
using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests.Security;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class AuthenticationE2ETests(E2EFixture stack)
{
    private static readonly string[] ReadRoutes = ["balance", "statement"];

    private static readonly string[] WriteRoutes = ["account", "entry", "reversal"];

    [E2EFact]
    public async Task EveryBusinessRoute_RefusesARequestWithoutToken_With401()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var setup = await SetUpAsync(api);

        foreach (var route in AllRoutes())
        {
            var response = await CallAsync(api, route, setup, anonymous: true);

            response.StatusCode.ShouldBe(401, $"{route}: {response.Body}");
            response.Code().ShouldBe("UNAUTHENTICATED");
            response.Header("WWW-Authenticate").ShouldNotBeNull().ShouldStartWith("Bearer");
        }
    }

    [E2EFact]
    public async Task EveryBusinessRoute_RefusesAnExpiredToken_AnUnknownSignerAndAWrongAudience_With401()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var setup = await SetUpAsync(api);
        using var stranger = RSA.Create(2048);
        var tokens = new Dictionary<string, string>
        {
            ["expired"] = E2ETokenFactory.CreateExpired(),
            ["signed by another key"] = E2ETokenFactory.Create(signer: stranger),
            ["another audience"] = E2ETokenFactory.Create(audience: "another-api")
        };

        foreach (var route in AllRoutes())
        {
            foreach (var (name, token) in tokens)
            {
                var response = await CallAsync(api, route, setup, token: token);

                response.StatusCode.ShouldBe(401, $"{route} with a token {name}: {response.Body}");
                response.Code().ShouldBe("UNAUTHENTICATED");
            }
        }
    }

    [E2EFact]
    public async Task TheReadScopeOpensOnlyTheReadRoutes_AndTheWriteScopeOnlyTheWriteRoutes()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var setup = await SetUpAsync(api);
        var readOnly = E2ETokenFactory.Create(E2ETokenFactory.ReadOnly);
        var writeOnly = E2ETokenFactory.Create(E2ETokenFactory.WriteOnly);

        foreach (var route in WriteRoutes)
        {
            var response = await CallAsync(api, route, setup, token: readOnly);

            response.StatusCode.ShouldBe(403, $"{route}: {response.Body}");
            response.Code().ShouldBe("FORBIDDEN");
            response.Header("WWW-Authenticate").ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.write\"");
        }

        foreach (var route in ReadRoutes)
        {
            var response = await CallAsync(api, route, setup, token: writeOnly);

            response.StatusCode.ShouldBe(403, $"{route}: {response.Body}");
            response.Code().ShouldBe("FORBIDDEN");
            response.Header("WWW-Authenticate").ShouldBe("Bearer error=\"insufficient_scope\", scope=\"ledger.read\"");
        }

        (await CallAsync(api, "balance", setup, token: readOnly)).StatusCode.ShouldBe(200);
        (await CallAsync(api, "statement", setup, token: readOnly)).StatusCode.ShouldBe(200);
        (await CallAsync(api, "entry", setup, token: writeOnly)).StatusCode.ShouldBe(201);
    }

    [E2EFact]
    public async Task ARefusedWrite_LeavesNoTraceInTheLedger()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("10.00");

        var denied = await api.CreditAsync(accountId, "99.00", token: E2ETokenFactory.Create(E2ETokenFactory.ReadOnly));
        var anonymous = await api.PostEntryAsync(accountId, "CREDIT", "99.00", anonymous: true);

        denied.StatusCode.ShouldBe(403, denied.Body);
        anonymous.StatusCode.ShouldBe(401, anonymous.Body);
        (await api.BalanceAsync(accountId)).Text("balance").ShouldBe("10.00");
        (await api.StatementAsync(accountId)).Json().GetProperty("items").GetArrayLength().ShouldBe(1);
        (await stack.Stack.Database().CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [E2EFact]
    public async Task TheClientThatWroteTheEntry_IsTheOneWhoseTokenSignedIt()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.CreateAccountAsync();
        const string Client = "e2e-pix-gateway";

        var response = await api.CreditAsync(accountId, "1.00", token: E2ETokenFactory.Create(client: Client));
        var stored = await stack.Stack.Database().ScalarAsync(
            "SELECT count(*) FROM ledger_entries WHERE account_id = @id AND client_id = @client",
            ("id", Guid.Parse(accountId)),
            ("client", Client));

        response.StatusCode.ShouldBe(201, response.Body);
        stored.ShouldBe(1);
    }

    private static IEnumerable<string> AllRoutes() => WriteRoutes.Concat(ReadRoutes);

    private static async Task<RouteSetup> SetUpAsync(E2EApi api)
    {
        var accountId = await api.FundedAccountAsync("100.00");
        var entryId = (await api.DebitAsync(accountId, "1.00")).Text("entryId");

        return new RouteSetup(accountId, entryId);
    }

    private static Task<E2EResponse> CallAsync(
        E2EApi api,
        string route,
        RouteSetup setup,
        string? token = null,
        bool anonymous = false)
    {
        return route switch
        {
            "account" => api.SendAsync(
                HttpMethod.Post,
                "/v1/accounts",
                $$"""{"holderDocument":"{{E2EApi.NewCpf()}}","currency":"BRL"}""",
                token: token,
                anonymous: anonymous),
            "entry" => api.PostEntryAsync(setup.AccountId, "CREDIT", "1.00", token: token, anonymous: anonymous),
            "reversal" => api.SendAsync(
                HttpMethod.Post,
                $"/v1/accounts/{setup.AccountId}/entries/{setup.EntryId}/reversals",
                null,
                E2EApi.NewKey(),
                token,
                anonymous),
            "balance" => api.BalanceAsync(setup.AccountId, token: token, anonymous: anonymous),
            "statement" => api.StatementAsync(setup.AccountId, token: token, anonymous: anonymous),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route name.")
        };
    }

    private sealed record RouteSetup(string AccountId, string EntryId);
}
