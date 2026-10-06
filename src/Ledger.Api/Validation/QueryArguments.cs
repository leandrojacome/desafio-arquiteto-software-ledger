namespace Ledger.Api.Validation;

internal sealed class QueryArguments(
    IReadOnlyDictionary<string, string> values,
    IReadOnlySet<string> repeated,
    IReadOnlyList<ValidationIssue> unknownFields)
{
    public IReadOnlyList<ValidationIssue> UnknownFields { get; } = unknownFields;

    public bool IsRepeated(string name) => repeated.Contains(name);

    public string? ValueOf(string name) => values.TryGetValue(name, out var value) ? value : null;
}
