using System.Net;
using System.Text.Json;

namespace Ledger.Api.IntegrationTests.Writes.Support;

internal sealed class ApiResponse
{
    private readonly Dictionary<string, string[]> _headers;

    private ApiResponse(HttpStatusCode status, string body, string? contentType, Dictionary<string, string[]> headers)
    {
        Status = status;
        Body = body;
        ContentType = contentType;
        _headers = headers;
    }

    public HttpStatusCode Status { get; }

    public int StatusCode => (int)Status;

    public string Body { get; }

    public string? ContentType { get; }

    public static async Task<ApiResponse> FromAsync(HttpResponseMessage response)
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

        return new ApiResponse(response.StatusCode, body, response.Content.Headers.ContentType?.MediaType, headers);
    }

    public JsonElement Json()
    {
        using var document = JsonDocument.Parse(Body);

        return document.RootElement.Clone();
    }

    public string Text(string property) => Json().GetProperty(property).GetString()
                                           ?? throw new InvalidOperationException($"{property} is null.");

    public bool HasHeader(string name) => _headers.ContainsKey(name);

    public string? Header(string name) => _headers.TryGetValue(name, out var values) ? string.Join(",", values) : null;

    public IReadOnlyList<string> HeaderValues(string name) =>
        _headers.TryGetValue(name, out var values) ? values : [];
}
