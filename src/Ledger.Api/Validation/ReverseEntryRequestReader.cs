using System.Collections.Frozen;

namespace Ledger.Api.Validation;

internal static class ReverseEntryRequestReader
{
    private const string DescriptionField = "description";

    private static readonly FrozenSet<string> KnownFields =
        new[] { DescriptionField }.ToFrozenSet(StringComparer.Ordinal);

    public static ReadResult<ReverseEntryInput> Read(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return ReadResult.Valid(new ReverseEntryInput(null));
        }

        using var parsed = JsonObjectBody.TryParse(body, out var rootIssue);

        if (parsed is null)
        {
            return ReadResult.Invalid<ReverseEntryInput>([rootIssue ?? FieldIssues.InvalidJson()]);
        }

        var issues = new List<ValidationIssue>();
        var description = BodyFields.Description(parsed, DescriptionField, issues);

        issues.AddRange(parsed.UnknownFields(KnownFields));

        return issues.Count == 0
            ? ReadResult.Valid(new ReverseEntryInput(description))
            : ReadResult.Invalid<ReverseEntryInput>(issues);
    }
}
