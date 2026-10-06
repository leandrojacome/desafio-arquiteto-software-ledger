using Ledger.EndToEnd.Tests.Support;

namespace Ledger.EndToEnd.Tests;

[Collection(E2ECollectionDefinition.Name)]
[Trait("Category", "E2E")]
public sealed class ProblemDetailsE2ETests(E2EFixture stack)
{
    private const string ProblemJson = "application/problem+json";
    private const string ProblemJsonUtf8 = "application/problem+json; charset=utf-8";

    [E2EFact]
    public async Task AnUnknownRoute_Answers404AsProblemDetailsWithTheCorrelationFields()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var response = await api.SendAsync(HttpMethod.Get, "/v1/does-not-exist");

        response.StatusCode.ShouldBe(404, response.Body);
        response.Header("Content-Type").ShouldNotBeNull().ShouldBe(ProblemJsonUtf8);
        response.Code().ShouldBe("NOT_FOUND");
        response.Text("title").ShouldBe("Recurso não encontrado");
        response.Text("detail").ShouldBe("O recurso solicitado não existe.");
        response.Body.ShouldNotContain("\\u00", Case.Insensitive);
        response.Text("correlationId").ShouldNotBeNullOrWhiteSpace();
        response.Text("traceId").ShouldNotBeNullOrWhiteSpace();
    }

    [E2EFact]
    public async Task DeleteOnTheLiveRoute_Answers405WithTheAllowHeader()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);

        var response = await api.SendAsync(HttpMethod.Delete, "/health/live", anonymous: true);

        response.StatusCode.ShouldBe(405, response.Body);
        response.Header("Allow").ShouldNotBeNull().Split(',', StringSplitOptions.TrimEntries).ShouldBe(["GET", "HEAD"]);
        response.Header("Content-Type").ShouldNotBeNull().ShouldStartWith(ProblemJson);
        response.Code().ShouldBe("METHOD_NOT_ALLOWED");
    }

    [E2EFact]
    public async Task EntriesCannotBeChangedOrRemovedThroughTheApi()
    {
        using var http = stack.CreateClient();
        var api = new E2EApi(http);
        var accountId = await api.FundedAccountAsync("50.00");
        var entryId = (await api.StatementAsync(accountId)).Json().GetProperty("items")[0].GetProperty("entryId").GetString();

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var response = await api.SendAsync(method, $"/v1/accounts/{accountId}/entries/{entryId}", "{}");

            response.StatusCode.ShouldBeOneOf(404, 405);
        }

        var stillThere = await api.StatementAsync(accountId);

        stillThere.Json().GetProperty("items").GetArrayLength().ShouldBe(1);
        (await api.BalanceAsync(accountId)).Text("balance").ShouldBe("50.00");
    }
}
