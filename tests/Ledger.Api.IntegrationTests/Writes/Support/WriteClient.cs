using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed class WriteClient(HttpClient http)
{
    public const string Brl = "BRL";

    private static int _documentIndex;

    public static string NewKey() => Guid.NewGuid().ToString("N");

    public static string AccountBody(string? document = null, string currency = Brl, string? overdraftLimit = null)
    {
        var holder = document ?? CpfFactory.Document(Interlocked.Increment(ref _documentIndex)).Normalized;
        var limit = overdraftLimit is null ? string.Empty : $",\"overdraftLimit\":\"{overdraftLimit}\"";

        return $"{{\"holderDocument\":\"{holder}\",\"currency\":\"{currency}\"{limit}}}";
    }

    public static string EntryBody(
        string type,
        string amount,
        string currency = Brl,
        string? occurredAt = null,
        string? description = null,
        string? reference = null)
    {
        var builder = new StringBuilder();

        builder.Append(CultureInfo.InvariantCulture, $"{{\"type\":\"{type}\",\"amount\":\"{amount}\",\"currency\":\"{currency}\"");

        if (occurredAt is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $",\"occurredAt\":\"{occurredAt}\"");
        }

        if (description is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $",\"description\":\"{description}\"");
        }

        if (reference is not null)
        {
            builder.Append(CultureInfo.InvariantCulture, $",\"reference\":\"{reference}\"");
        }

        builder.Append('}');

        return builder.ToString();
    }

    public async Task<ApiResponse> SendAsync(
        HttpMethod method,
        string path,
        string? body = null,
        WriteRequestOptions? options = null)
    {
        var settings = options ?? new WriteRequestOptions();

        using var request = new HttpRequestMessage(method, path);

        if (!settings.Anonymous)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token ?? TestTokenFactory.Create());
        }

        if (settings.IdempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", settings.IdempotencyKey);
        }

        foreach (var repeated in settings.RepeatedIdempotencyKeys)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", repeated);
        }

        if (settings.CorrelationId is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", settings.CorrelationId);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.Remove("Content-Type");

            if (settings.ContentType is not null)
            {
                request.Content.Headers.TryAddWithoutValidation("Content-Type", settings.ContentType);
            }
        }

        using var response = await http.SendAsync(request, CancellationToken.None);

        return await ApiResponse.FromAsync(response);
    }

    public Task<ApiResponse> PostAccountAsync(string? body = null, WriteRequestOptions? options = null) =>
        SendAsync(HttpMethod.Post, "/v1/accounts", body ?? AccountBody(), options);

    public Task<ApiResponse> PostEntryAsync(string accountId, string body, string? key, WriteRequestOptions? options = null) =>
        SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries",
            body,
            (options ?? new WriteRequestOptions()) with { IdempotencyKey = key });

    public Task<ApiResponse> PostReversalAsync(
        string accountId,
        string entryId,
        string? key,
        string? body = null,
        WriteRequestOptions? options = null) =>
        SendAsync(
            HttpMethod.Post,
            $"/v1/accounts/{accountId}/entries/{entryId}/reversals",
            body,
            (options ?? new WriteRequestOptions()) with { IdempotencyKey = key });

    public async Task<string> CreateAccountAsync(string currency = Brl, string? overdraftLimit = null)
    {
        var response = await PostAccountAsync(AccountBody(currency: currency, overdraftLimit: overdraftLimit));

        response.StatusCode.ShouldBe(201, response.Body);

        return response.Text("accountId");
    }

    public Task<ApiResponse> CreditAsync(string accountId, string amount, string? key = null) =>
        PostEntryAsync(accountId, EntryBody("CREDIT", amount), key ?? NewKey());

    public Task<ApiResponse> DebitAsync(string accountId, string amount, string? key = null) =>
        PostEntryAsync(accountId, EntryBody("DEBIT", amount), key ?? NewKey());

    public async Task<string> CreateFundedAccountAsync(string amount, string? overdraftLimit = null)
    {
        var accountId = await CreateAccountAsync(overdraftLimit: overdraftLimit);
        var funding = await CreditAsync(accountId, amount);

        funding.StatusCode.ShouldBe(201, funding.Body);

        return accountId;
    }
}
