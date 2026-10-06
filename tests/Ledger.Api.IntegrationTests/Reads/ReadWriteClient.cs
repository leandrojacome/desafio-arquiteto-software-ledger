using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Domain.Accounts;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadWriteClient : IDisposable
{
    private readonly HttpClient _client;

    private ReadWriteClient(HttpClient client)
    {
        _client = client;
    }

    public static ReadWriteClient For(LedgerApiFactory factory) => new(factory.Authenticated());

    public async Task<ReadResponse> PostEntryAsync(
        AccountId accountId,
        string key,
        string type,
        string amount,
        string? description = null,
        string? reference = null)
    {
        var text = description is null ? "null" : $"\"{description}\"";
        var referenceText = reference is null ? "null" : $"\"{reference}\"";
        var body = $$"""{"type":"{{type}}","amount":"{{amount}}","currency":"BRL","description":{{text}},"reference":{{referenceText}}}""";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/accounts/{accountId}/entries")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("Idempotency-Key", key);

        using var response = await _client.SendAsync(request, CancellationToken.None);

        return await ReadResponse.FromAsync(response, CancellationToken.None);
    }

    public void Dispose() => _client.Dispose();
}
