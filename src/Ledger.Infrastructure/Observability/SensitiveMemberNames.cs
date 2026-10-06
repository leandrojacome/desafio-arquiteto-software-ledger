using System.Text;

namespace Ledger.Infrastructure.Observability;

internal static class SensitiveMemberNames
{
    public const string Mask = "***";

    private static readonly string[] Fragments =
    [
        "password",
        "passwd",
        "secret",
        "token",
        "authorization",
        "connectionstring",
        "apikey",
        "holderdocument",
        "document",
        "cpf",
        "cnpj",
        "idempotencykey",
        "encryptionkey",
        "blindindex"
    ];

    public static bool IsSensitive(string memberName)
    {
        var normalized = Normalize(memberName);

        foreach (var fragment in Fragments)
        {
            if (normalized.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string memberName)
    {
        var builder = new StringBuilder(memberName.Length);

        foreach (var character in memberName)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
