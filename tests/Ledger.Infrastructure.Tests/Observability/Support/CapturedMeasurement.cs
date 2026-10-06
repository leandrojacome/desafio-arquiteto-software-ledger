namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed record CapturedMeasurement(
    string Instrument,
    string? Unit,
    double Value,
    IReadOnlyDictionary<string, object?> Tags)
{
    public string? Tag(string key) => Tags.TryGetValue(key, out var value) ? value as string : null;

    public bool Has(params (string Key, string Value)[] expected)
    {
        return expected.All(pair => Tag(pair.Key) == pair.Value);
    }
}
