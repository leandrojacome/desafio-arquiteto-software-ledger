using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Writes.Support;

namespace Ledger.Api.IntegrationTests.Writes.Entries;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed partial class EntryEncodingTests(PostgresFixture postgres)
{
    private const string Description = "depósito em conta";
    private const string JsonUtf8 = "application/json; charset=utf-8";

    private readonly WriteTestData _data = new(postgres);

    [DockerFact]
    public async Task AccentedDescription_SentAsUtf8Bytes_IsRegisteredAndComesBackReadable()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes(WriteClient.EntryBody("CREDIT", "80.00", description: Description));

        var response = await PostEntryAsync(http, accountId, body, JsonUtf8, WriteClient.NewKey());

        response.Status.ShouldBe(HttpStatusCode.Created, response.Text);
        response.ContentType.ShouldBe(JsonUtf8);
        response.Bytes.AsSpan().IndexOf("\"description\":\"depósito em conta\""u8).ShouldBeGreaterThan(0);
        AccentedLetterEscape().IsMatch(response.Text).ShouldBeFalse();
        Json(response).GetProperty("description").GetString().ShouldBe(Description);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task AccentedDescription_WithoutDeclaringTheCharset_IsAlsoReadAsUtf8()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes(WriteClient.EntryBody("CREDIT", "80.00", description: Description));

        var response = await PostEntryAsync(http, accountId, body, "application/json", WriteClient.NewKey());

        response.Status.ShouldBe(HttpStatusCode.Created, response.Text);
        Json(response).GetProperty("description").GetString().ShouldBe(Description);
    }

    [DockerFact]
    public async Task AccentedDescription_ComesBackReadableOnTheStatement()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes(WriteClient.EntryBody("CREDIT", "80.00", description: Description));

        (await PostEntryAsync(http, accountId, body, JsonUtf8, WriteClient.NewKey())).Status.ShouldBe(HttpStatusCode.Created);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/accounts/{accountId}/entries");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());
        using var message = await http.SendAsync(request, CancellationToken.None);
        var bytes = await message.Content.ReadAsByteArrayAsync(CancellationToken.None);

        message.StatusCode.ShouldBe(HttpStatusCode.OK, Encoding.UTF8.GetString(bytes));
        bytes.AsSpan().IndexOf("\"description\":\"depósito em conta\""u8).ShouldBeGreaterThan(0);
        AccentedLetterEscape().IsMatch(Encoding.UTF8.GetString(bytes)).ShouldBeFalse();
    }

    [DockerFact]
    public async Task TheSameDescription_WrittenAsRawUtf8OrAsAnEscape_IsTheSameRequestForTheKey()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        var key = WriteClient.NewKey();
        var raw = Encoding.UTF8.GetBytes(WriteClient.EntryBody("CREDIT", "80.00", description: Description));
        var escaped = Encoding.ASCII.GetBytes(
            WriteClient.EntryBody("CREDIT", "80.00", description: "dep\\u00f3sito em conta"));

        var first = await PostEntryAsync(http, accountId, raw, JsonUtf8, key);
        var second = await PostEntryAsync(http, accountId, escaped, "application/json", key);

        first.Status.ShouldBe(HttpStatusCode.Created, first.Text);
        second.Status.ShouldBe(HttpStatusCode.Created, second.Text);
        second.Replayed.ShouldBe("true");
        second.Text.ShouldBe(first.Text);
        (await _data.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task AccentedDescription_OnAReversal_IsAcceptedAndComesBackReadable()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateFundedAccountAsync("100.00");
        var original = await client.DebitAsync(accountId, "10.00");
        using var http = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes("{\"description\":\"estorno de depósito em conta\"}");

        var response = await PostReversalAsync(http, accountId, original.Text("entryId"), body, WriteClient.NewKey());

        response.Status.ShouldBe(HttpStatusCode.Created, response.Text);
        response.Bytes.AsSpan().IndexOf("\"description\":\"estorno de depósito em conta\""u8).ShouldBeGreaterThan(0);
        Json(response).GetProperty("description").GetString().ShouldBe("estorno de depósito em conta");
    }

    [DockerFact]
    public async Task Body_WithBytesThatAreNotUtf8_IsRefusedAsAValidationProblemAndRegistersNothing()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        byte[] body =
        [
            .. Encoding.UTF8.GetBytes("{\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"description\":\"dep"),
            0xF3,
            .. Encoding.UTF8.GetBytes("sito\"}")
        ];

        var response = await PostEntryAsync(http, accountId, body, JsonUtf8, WriteClient.NewKey());

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Text);
        Json(response).GetProperty("code").GetString().ShouldBe("VALIDATION_FAILED");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [DockerFact]
    public async Task ValidationProblem_IsWrittenAsReadableUtf8WithTheSingleQuotesOfTheFieldNamesEscaped()
    {
        await using var factory = new WriteApiFactory(postgres);
        var client = new WriteClient(factory.CreateClient());
        var accountId = await client.CreateAccountAsync();
        using var http = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes(WriteClient.EntryBody("CREDIT", "1.001", description: Description));

        var response = await PostEntryAsync(http, accountId, body, JsonUtf8, WriteClient.NewKey());

        response.Status.ShouldBe(HttpStatusCode.BadRequest, response.Text);
        response.ContentType.ShouldBe("application/problem+json; charset=utf-8");
        response.Bytes.AsSpan().IndexOf("O campo \\u0027amount\\u0027 deve ter no máximo 2 casas decimais."u8).ShouldBeGreaterThan(0);
        response.Bytes.AsSpan().IndexOf("\"title\":\"Falha na validação da requisição\""u8).ShouldBeGreaterThan(0);
        AccentedLetterEscape().IsMatch(response.Text).ShouldBeFalse();
        Json(response).GetProperty("errors").EnumerateArray().Single().GetProperty("message").GetString()
            .ShouldBe("O campo 'amount' deve ter no máximo 2 casas decimais.");
        (await _data.CountEntriesAsync(accountId)).ShouldBe(0);
    }

    [GeneratedRegex(@"\\u00[89A-Fa-f][0-9A-Fa-f]", RegexOptions.CultureInvariant)]
    private static partial Regex AccentedLetterEscape();

    private static JsonElement Json(RawResponse response)
    {
        using var document = JsonDocument.Parse(response.Bytes);

        return document.RootElement.Clone();
    }

    private static Task<RawResponse> PostEntryAsync(
        HttpClient http,
        string accountId,
        byte[] body,
        string contentType,
        string key) =>
        PostAsync(http, $"/v1/accounts/{accountId}/entries", body, contentType, key);

    private static Task<RawResponse> PostReversalAsync(
        HttpClient http,
        string accountId,
        string entryId,
        byte[] body,
        string key) =>
        PostAsync(http, $"/v1/accounts/{accountId}/entries/{entryId}/reversals", body, JsonUtf8, key);

    private static async Task<RawResponse> PostAsync(
        HttpClient http,
        string path,
        byte[] body,
        string contentType,
        string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokenFactory.Create());
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);

        using var response = await http.SendAsync(request, CancellationToken.None);
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        var replayed = response.Headers.TryGetValues("Idempotent-Replayed", out var values) ? values.Single() : null;

        return new RawResponse(response.StatusCode, response.Content.Headers.ContentType?.ToString(), replayed, bytes);
    }

    private sealed record RawResponse(HttpStatusCode Status, string? ContentType, string? Replayed, byte[] Bytes)
    {
        public string Text => Encoding.UTF8.GetString(Bytes);
    }
}
