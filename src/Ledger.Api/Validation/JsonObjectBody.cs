using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ledger.Api.Validation;

internal sealed partial class JsonObjectBody : IDisposable
{
    private const string UnknownFieldName = "(unknown)";

    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false };

    private readonly JsonDocument _document;
    private readonly Dictionary<string, JsonElement> _properties;

    private JsonObjectBody(JsonDocument document, Dictionary<string, JsonElement> properties)
    {
        _document = document;
        _properties = properties;
    }

    public static JsonObjectBody? TryParse(ReadOnlySpan<byte> body, out ValidationIssue? issue)
    {
        issue = null;

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body.ToArray(), ParseOptions);
        }
        catch (JsonException)
        {
            issue = FieldIssues.InvalidJson();

            return null;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            issue = FieldIssues.InvalidJson();

            return null;
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        try
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                properties[property.Name] = property.Value;
            }
        }
        catch (InvalidOperationException)
        {
            document.Dispose();
            issue = FieldIssues.InvalidJson();

            return null;
        }

        return new JsonObjectBody(document, properties);
    }

    public bool TryGet(string name, out JsonElement value)
    {
        return _properties.TryGetValue(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    public IEnumerable<ValidationIssue> UnknownFields(IReadOnlySet<string> known)
    {
        foreach (var name in _properties.Keys)
        {
            if (!known.Contains(name))
            {
                yield return FieldIssues.UnknownField(SafeName().IsMatch(name) ? name : UnknownFieldName);
            }
        }
    }

    public void Dispose() => _document.Dispose();

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
