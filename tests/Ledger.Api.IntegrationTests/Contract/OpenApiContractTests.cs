using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ledger.Api.ErrorHandling;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Contract;

[Trait("Category", "Integration")]
[Trait("Category", "Contract")]
public sealed partial class OpenApiContractTests
{
    private const string UpdateVariable = "LEDGER_OPENAPI_UPDATE";

    private const string Instruction =
        "The OpenAPI document that the API produces differs from docs/05-contratos/openapi.v1.json. " +
        "If the change of the contract is intended, regenerate the file with `LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter \"FullyQualifiedName~OpenApiContractTests\"` " +
        "and review the difference before committing it. If it is not intended, fix the endpoint or the contract type.";

    private static readonly Dictionary<string, int> StatusOfCode = new(StringComparer.Ordinal)
    {
        [ProblemCatalog.ValidationFailed] = 400,
        [ProblemCatalog.IdempotencyKeyRequired] = 400,
        [ProblemCatalog.InvalidAsOf] = 400,
        [ProblemCatalog.Unauthenticated] = 401,
        [ProblemCatalog.Forbidden] = 403,
        [ProblemCatalog.AccountNotFound] = 404,
        [ProblemCatalog.EntryNotFound] = 404,
        [ProblemCatalog.EntryAlreadyReversed] = 409,
        [ProblemCatalog.PayloadTooLarge] = 413,
        [ProblemCatalog.UnsupportedMediaType] = 415,
        [ProblemCatalog.InsufficientFunds] = 422,
        [ProblemCatalog.CurrencyMismatch] = 422,
        [ProblemCatalog.IdempotencyKeyReused] = 422,
        [ProblemCatalog.EntryNotReversible] = 422,
        [ProblemCatalog.RateLimited] = 429,
        [ProblemCatalog.InternalError] = 500,
        [ProblemCatalog.ServiceUnavailable] = 503
    };

    [Fact]
    public async Task TheGeneratedDocument_IsTheVersionedFile()
    {
        var generated = OpenApiDocument.Canonicalize(await OpenApiDocument.GenerateAsync());

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "true")
        {
            await File.WriteAllTextAsync(OpenApiDocument.VersionedPath, generated, CancellationToken.None);
        }

        File.Exists(OpenApiDocument.VersionedPath).ShouldBeTrue(Instruction);

        var versioned = OpenApiDocument.Canonicalize(
            JsonNode.Parse(await File.ReadAllTextAsync(OpenApiDocument.VersionedPath, CancellationToken.None))
            ?? throw new InvalidOperationException("The versioned OpenAPI file is empty."));

        generated.ShouldBe(versioned, $"{Instruction} First difference at {OpenApiDocument.FirstDifference(versioned, generated)}.");
    }

    [Fact]
    public async Task TheVersionedFile_IsStoredInTheCanonicalFormat()
    {
        var stored = await File.ReadAllTextAsync(OpenApiDocument.VersionedPath, CancellationToken.None);
        var canonical = OpenApiDocument.Canonicalize(JsonNode.Parse(stored) ?? throw new InvalidOperationException("Empty."));

        stored.ShouldBe(canonical, "docs/05-contratos/openapi.v1.json must be written by `LEDGER_OPENAPI_UPDATE=true dotnet test tests/Ledger.Api.IntegrationTests --filter \"FullyQualifiedName~OpenApiContractTests\"`.");
    }

    [Fact]
    public async Task TheDocument_ListsExactlyTheFiveRoutesOfTheContract()
    {
        var document = await OpenApiDocument.GenerateAsync();

        var found = document["paths"]!.AsObject()
            .SelectMany(path => path.Value!.AsObject().Select(operation => $"{operation.Key.ToUpperInvariant()} {path.Key}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        var expected = RouteCatalog.BusinessRoutes
            .Select(route => $"{route.Method.Method} {route.Template}")
            .Order(StringComparer.Ordinal)
            .ToList();

        found.ShouldBe(expected);
    }

    [Fact]
    public async Task EveryOperation_HasAnIdentifierASummaryATagAndTheBearerRequirement()
    {
        var document = await OpenApiDocument.GenerateAsync();

        foreach (var (path, operation) in Operations(document))
        {
            operation["operationId"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace(path);
            operation["summary"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace(path);
            operation["tags"]!.AsArray().ShouldHaveSingleItem(path);
            operation["security"]!.AsArray().Single()!["Bearer"].ShouldNotBeNull(path);
        }
    }

    [Fact]
    public async Task TheBearerScheme_IsDeclaredAsAJwt()
    {
        var document = await OpenApiDocument.GenerateAsync();

        var bearer = document["components"]!["securitySchemes"]!["Bearer"]!;

        bearer["type"]!.GetValue<string>().ShouldBe("http");
        bearer["scheme"]!.GetValue<string>().ShouldBe("bearer");
        bearer["bearerFormat"]!.GetValue<string>().ShouldBe("JWT");
    }

    [Fact]
    public async Task TheRoutesThatWrite_AcceptABodyAndDocumentTheIdempotencyKey()
    {
        var document = await OpenApiDocument.GenerateAsync();

        Body(document, "/v1/accounts", "post")["required"]!.GetValue<bool>().ShouldBeTrue();
        Body(document, "/v1/accounts/{accountId}/entries", "post")["required"]!.GetValue<bool>().ShouldBeTrue();
        IsRequired(Body(document, "/v1/accounts/{accountId}/entries/{entryId}/reversals", "post")).ShouldBeFalse();

        IsRequired(Header(document, "/v1/accounts", "Idempotency-Key")).ShouldBeFalse();
        IsRequired(Header(document, "/v1/accounts/{accountId}/entries", "Idempotency-Key")).ShouldBeTrue();
        IsRequired(Header(document, "/v1/accounts/{accountId}/entries/{entryId}/reversals", "Idempotency-Key")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/v1/accounts", "holderDocument,currency,overdraftLimit", "holderDocument,currency")]
    [InlineData("/v1/accounts/{accountId}/entries", "type,amount,currency,occurredAt,description,reference", "type,amount,currency")]
    [InlineData("/v1/accounts/{accountId}/entries/{entryId}/reversals", "description", "")]
    public async Task TheRequestSchemas_ListTheirPropertiesAndRefuseTheUnknownOnes(string path, string properties, string required)
    {
        var document = await OpenApiDocument.GenerateAsync();

        var schema = Body(document, path, "post")["content"]!["application/json"]!["schema"]!;

        schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse(path);
        schema["properties"]!.AsObject().Select(property => property.Key).ShouldBe(properties.Split(','), path);
        (schema["required"]?.AsArray().Select(name => name!.GetValue<string>()) ?? []).ShouldBe(
            required.Split(',', StringSplitOptions.RemoveEmptyEntries),
            path);
    }

    [Theory]
    [InlineData("/v1/accounts")]
    [InlineData("/v1/accounts/{accountId}/entries")]
    [InlineData("/v1/accounts/{accountId}/entries/{entryId}/reversals")]
    public async Task TheCreatedResponseOfAWrite_DocumentsTheReplayHeader(string path)
    {
        var document = await OpenApiDocument.GenerateAsync();

        var created = document["paths"]![path]!["post"]!["responses"]!["201"]!;

        created["headers"]!["Idempotent-Replayed"]!["schema"]!["enum"]!.AsArray().Single()!.GetValue<string>()
            .ShouldBe("true");
        created["headers"]!["Location"].ShouldNotBeNull();
    }

    [Fact]
    public async Task TheReadRoutes_DocumentTheirQueryParameters()
    {
        var document = await OpenApiDocument.GenerateAsync();

        QueryNames(document, "/v1/accounts/{accountId}/balance").ShouldBe(["asOf"]);
        QueryNames(document, "/v1/accounts/{accountId}/entries").ShouldBe(["from", "to", "limit", "cursor"]);
    }

    [Fact]
    public async Task TheRetryAfterHeader_IsDocumentedWhereTheApiSendsIt()
    {
        var document = await OpenApiDocument.GenerateAsync();

        foreach (var (path, operation) in Operations(document))
        {
            operation["responses"]!["429"]!["headers"]!["Retry-After"].ShouldNotBeNull(path);
            operation["responses"]!["503"]!["headers"]!["Retry-After"].ShouldNotBeNull(path);
            operation["responses"]!["401"]!["headers"]!["WWW-Authenticate"].ShouldNotBeNull(path);
        }
    }

    [Fact]
    public async Task TheProblemSchema_ListsEveryCodeOfTheCatalog()
    {
        var document = await OpenApiDocument.GenerateAsync();

        var codes = document["components"]!["schemas"]!["ProblemResponse"]!["properties"]!["code"]!["enum"]!
            .AsArray()
            .Select(code => code!.GetValue<string>())
            .ToList();

        codes.ShouldBe(ProblemCatalog.Codes.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task EveryErrorResponse_IsAProblemDetailsDocument()
    {
        var document = await OpenApiDocument.GenerateAsync();

        foreach (var (path, operation) in Operations(document))
        {
            foreach (var (status, response) in operation["responses"]!.AsObject().Where(item => item.Key != "200" && item.Key != "201"))
            {
                var content = response!["content"]!.AsObject();

                content.Single().Key.ShouldBe("application/problem+json", $"{path} {status}");
                content.Single().Value!["schema"]!["$ref"]!.GetValue<string>().ShouldBe("#/components/schemas/ProblemResponse");
            }
        }
    }

    [Fact]
    public void EveryProblemCodeOfTheDocument_BelongsToTheCatalogWithTheStatusItIsAnsweredWith()
    {
        using var factory = new LedgerApiFactory();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var documented = endpoints.SelectMany(endpoint => endpoint.Metadata.OfType<ProblemCodesMetadata>()).ToList();

        documented.ShouldNotBeEmpty();

        foreach (var metadata in documented)
        {
            metadata.Codes.ShouldNotBeEmpty();

            foreach (var code in metadata.Codes)
            {
                ProblemCatalog.Contains(code).ShouldBeTrue(code);
                StatusOfCode[code].ShouldBe(metadata.Status, code);
            }
        }
    }

    [Fact]
    public async Task EveryOperation_ListsItsProblemCodesInTheResponseDescription()
    {
        var document = await OpenApiDocument.GenerateAsync();

        var description = document["paths"]!["/v1/accounts/{accountId}/entries"]!["post"]!["responses"]!["422"]!["description"]!
            .GetValue<string>();

        description.ShouldContain("INSUFFICIENT_FUNDS");
        description.ShouldContain("CURRENCY_MISMATCH");
        description.ShouldContain("IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task EveryDescriptionAndSummary_IsWrittenInPortuguese()
    {
        var document = await OpenApiDocument.GenerateAsync();
        var texts = new List<string>();

        CollectTexts(document, texts);

        texts.Count.ShouldBeGreaterThan(50);

        foreach (var text in texts)
        {
            EnglishWord().IsMatch(text).ShouldBeFalse(text);
        }
    }

    [Fact]
    public async Task TheDescriptionsOfTheInstants_ExplainTheTimeZoneAndKeepTheProtocolNamesUntouched()
    {
        var document = await OpenApiDocument.GenerateAsync();
        var texts = new List<string>();

        CollectTexts(document, texts);

        var instants = texts.Where(text => text.Contains("fuso horário informado", StringComparison.Ordinal)).ToList();

        instants.Count.ShouldBeGreaterThanOrEqualTo(4);
        texts.ShouldContain(text => text.Contains("Idempotent-Replayed: true", StringComparison.Ordinal));
        texts.ShouldContain(text => text.Contains("Código do problema: UNAUTHENTICATED.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDocument_NeverCarriesAServerAddress()
    {
        var document = await OpenApiDocument.GenerateAsync();

        (document["servers"]?.AsArray().Count ?? 0).ShouldBe(0);
    }

    [Fact]
    public async Task TheHealthRoutesAndTheDocumentItself_AreNotPartOfTheContract()
    {
        var document = await OpenApiDocument.GenerateAsync();

        document["paths"]!.AsObject().Select(path => path.Key).ShouldAllBe(path => path.StartsWith("/v1/", StringComparison.Ordinal));
    }

    [GeneratedRegex(
        @"\b(the|of|and|is|are|with|for|that|this|it|by|be|can|not|or|an|when|only|never|always|in)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EnglishWord();

    private static void CollectTexts(JsonNode? node, List<string> texts)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (name, value) in item)
                {
                    if (name is "description" or "summary" && value is JsonValue text && text.TryGetValue<string>(out var content))
                    {
                        texts.Add(content);
                    }
                    else
                    {
                        CollectTexts(value, texts);
                    }
                }

                break;
            case JsonArray list:
                foreach (var element in list)
                {
                    CollectTexts(element, texts);
                }

                break;
        }
    }

    private static IEnumerable<(string Path, JsonNode Operation)> Operations(JsonNode document)
    {
        foreach (var (path, item) in document["paths"]!.AsObject())
        {
            foreach (var (_, operation) in item!.AsObject())
            {
                yield return (path, operation!);
            }
        }
    }

    private static JsonNode Body(JsonNode document, string path, string method) =>
        document["paths"]![path]![method]!["requestBody"]!;

    private static JsonNode Header(JsonNode document, string path, string name) =>
        document["paths"]![path]!["post"]!["parameters"]!.AsArray()
            .Single(parameter => parameter!["name"]!.GetValue<string>() == name && parameter["in"]!.GetValue<string>() == "header")!;

    private static bool IsRequired(JsonNode parameter) => parameter["required"]?.GetValue<bool>() ?? false;

    private static List<string> QueryNames(JsonNode document, string path) =>
        [.. document["paths"]![path]!["get"]!["parameters"]!.AsArray()
            .Where(parameter => parameter!["in"]!.GetValue<string>() == "query")
            .Select(parameter => parameter!["name"]!.GetValue<string>())];
}
