using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Integration")]
public sealed partial class ProblemDetailsTests(LedgerApiFactory factory) : IClassFixture<LedgerApiFactory>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    [Fact]
    public async Task UnknownRoute_WhenAuthenticated_ReturnsProblemJsonFollowingTheCatalog()
    {
        using var client = factory.Authenticated();

        using var response = await client.GetAsync("/does-not-exist", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldBeProblemJson(response);

        using var problem = await ReadJson(response);
        var root = problem.RootElement;

        root.GetProperty("code").GetString().ShouldBe("NOT_FOUND");
        root.GetProperty("title").GetString().ShouldBe("Recurso não encontrado");
        root.GetProperty("detail").GetString().ShouldBe("O recurso solicitado não existe.");
        root.GetProperty("status").GetInt32().ShouldBe(404);
        root.GetProperty("type").GetString().ShouldBe("https://ledger.bank.internal/problems/not-found");
        root.GetProperty("instance").GetString().ShouldBe("/does-not-exist");
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UnknownRoute_WithoutToken_IsDeniedByDefaultWithoutRevealingTheRoutes()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/does-not-exist", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldBeProblemJson(response);

        using var problem = await ReadJson(response);

        problem.RootElement.GetProperty("code").GetString().ShouldBe("UNAUTHENTICATED");
        problem.RootElement.GetProperty("title").GetString().ShouldBe("Autenticação necessária");
    }

    [Fact]
    public async Task ProblemResponse_DeclaresUtf8AndKeepsTheAccentsReadable()
    {
        using var client = factory.Authenticated();

        using var response = await client.GetAsync("/does-not-exist", CancellationToken.None);

        var contentType = response.Content.Headers.ContentType.ShouldNotBeNull();
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        var body = System.Text.Encoding.UTF8.GetString(bytes);

        contentType.MediaType.ShouldBe("application/problem+json");
        contentType.CharSet.ShouldBe("utf-8");
        response.Content.Headers.ContentType.ToString().ShouldBe("application/problem+json; charset=utf-8");
        body.ShouldContain("Recurso não encontrado");
        body.ShouldContain("O recurso solicitado não existe.");
        body.ShouldNotContain("\\u00", Case.Insensitive);
        bytes.AsSpan().IndexOf("não"u8).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ProblemBody_RepeatsTheCorrelationIdOfTheResponseHeader()
    {
        using var client = factory.Authenticated();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add(CorrelationHeader, "9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");

        using var response = await client.SendAsync(request, CancellationToken.None);

        using var problem = await ReadJson(response);

        response.Headers.GetValues(CorrelationHeader).ShouldHaveSingleItem()
            .ShouldBe("9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");
        problem.RootElement.GetProperty("correlationId").GetString().ShouldBe("9f3c1a7e2b4d4f60a1c8e5d7b3a29f10");
    }

    [Fact]
    public async Task CorrelationId_OnAnUnauthorizedResponse_IsStillReturned()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/accounts/not-an-account/balance");
        request.Headers.Add(CorrelationHeader, "client-supplied-id-0001");

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.GetValues(CorrelationHeader).ShouldHaveSingleItem().ShouldBe("client-supplied-id-0001");
    }

    [Fact]
    public async Task CorrelationId_WhenAbsent_IsGenerated()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        var generated = response.Headers.GetValues(CorrelationHeader).ShouldHaveSingleItem();

        GeneratedCorrelationId().IsMatch(generated).ShouldBeTrue();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("contains spaces in the value")]
    [InlineData("has\u0001control-characters")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public async Task CorrelationId_WhenMalformed_IsDiscardedAndReplaced(string received)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationHeader, received);

        using var response = await client.SendAsync(request, CancellationToken.None);

        var returned = response.Headers.GetValues(CorrelationHeader).ShouldHaveSingleItem();

        returned.ShouldNotBe(received);
        GeneratedCorrelationId().IsMatch(returned).ShouldBeTrue();
    }

    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedCorrelationId();

    private static void ShouldBeProblemJson(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType;

        contentType.ShouldNotBeNull();
        contentType.MediaType.ShouldBe("application/problem+json");
        contentType.CharSet.ShouldBe("utf-8");
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        return JsonDocument.Parse(body);
    }
}
