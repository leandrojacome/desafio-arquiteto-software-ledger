using System.Net;
using System.Text.Json;

namespace Ledger.EndToEnd.Tests.Support;

internal sealed class E2EResponse
{
    private readonly Dictionary<string, string[]> _headers;

    private E2EResponse(HttpStatusCode status, string body, TimeSpan elapsed, Dictionary<string, string[]> headers)
    {
        Status = status;
        Body = body;
        Elapsed = elapsed;
        _headers = headers;
    }

    public HttpStatusCode Status { get; }

    public int StatusCode => (int)Status;

    public string Body { get; }

    public TimeSpan Elapsed { get; }

    public static async Task<E2EResponse> FromAsync(HttpResponseMessage response, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        return new E2EResponse(response.StatusCode, body, elapsed, headers);
    }

    public JsonElement Json()
    {
        using var document = JsonDocument.Parse(Body);

        return document.RootElement.Clone();
    }

    public string Text(string property) => Json().GetProperty(property).GetString()
                                           ?? throw new InvalidOperationException($"{property} is null.");

    public string Code() => Text("code");

    public string? Header(string name) => _headers.TryGetValue(name, out var values) ? string.Join(",", values) : null;
}
