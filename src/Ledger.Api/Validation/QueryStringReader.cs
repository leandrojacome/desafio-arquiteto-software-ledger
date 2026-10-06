using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Ledger.Api.Validation;

internal static partial class QueryStringReader
{
    private const string UnknownFieldName = "(unknown)";
    private const string UnknownFieldMessage = "O parâmetro não é suportado.";

    public static QueryArguments Read(string? queryString, IReadOnlySet<string> knownNames)
    {
        ArgumentNullException.ThrowIfNull(knownNames);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeated = new HashSet<string>(StringComparer.Ordinal);
        var unknownSeen = new HashSet<string>(StringComparer.Ordinal);
        var unknownFields = new List<ValidationIssue>();

        foreach (var pair in new QueryStringEnumerable(queryString))
        {
            var name = pair.DecodeName().ToString();

            if (!knownNames.Contains(name))
            {
                if (unknownSeen.Add(name))
                {
                    unknownFields.Add(new ValidationIssue(
                        ReportedName(name),
                        ValidationReasons.UnknownField,
                        UnknownFieldMessage));
                }

                continue;
            }

            if (!values.TryAdd(name, pair.DecodeValue().ToString()))
            {
                repeated.Add(name);
            }
        }

        return new QueryArguments(values, repeated, unknownFields);
    }

    private static string ReportedName(string name) => SafeName().IsMatch(name) ? name : UnknownFieldName;

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeName();
}
