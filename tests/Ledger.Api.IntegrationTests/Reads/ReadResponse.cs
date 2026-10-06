using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Ledger.Api.IntegrationTests.Reads;

internal sealed class ReadResponse : IDisposable
{
    private readonly JsonDocument? _document;

    private ReadResponse(
        HttpStatusCode status,
        string body,
        string? contentType,
        IReadOnlyDictionary<string, string[]> headers,
        JsonDocument? document)
    {
        Status = status;
        Body = body;
        ContentType = contentType;
        Headers = headers;
        _document = document;
    }

    public HttpStatusCode Status { get; }

    public string Body { get; }

    public string? ContentType { get; }

    public IReadOnlyDictionary<string, string[]> Headers { get; }

    public JsonElement Json => _document?.RootElement
                               ?? throw new InvalidOperationException($"The response is not JSON: {Body}");

    public string? Header(string name) =>
        Headers.TryGetValue(name, out var values) ? string.Join(", ", values) : null;

    public bool HasHeader(string name) => Headers.ContainsKey(name);

    public string Text(string property) => Json.GetProperty(property).GetString() ?? string.Empty;

    public bool IsNull(string property) => Json.GetProperty(property).ValueKind == JsonValueKind.Null;

    public bool Has(string property) => Json.TryGetProperty(property, out _);

    public decimal Money(string property) =>
        decimal.Parse(Text(property), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static async Task<ReadResponse> FromAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = await message.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in message.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        foreach (var header in message.Content.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        var contentType = message.Content.Headers.ContentType?.MediaType;
        var document = IsJson(contentType) && body.Length > 0 ? JsonDocument.Parse(body) : null;

        return new ReadResponse(message.StatusCode, body, contentType, headers, document);
    }

    public void Dispose() => _document?.Dispose();

    private static bool IsJson(string? contentType) =>
        contentType is not null && (contentType.EndsWith("/json", StringComparison.Ordinal)
                                    || contentType.EndsWith("+json", StringComparison.Ordinal));
}
