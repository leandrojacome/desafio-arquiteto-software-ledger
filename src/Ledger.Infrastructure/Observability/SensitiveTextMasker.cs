using System.Text.RegularExpressions;

namespace Ledger.Infrastructure.Observability;

internal static partial class SensitiveTextMasker
{
    private const int TimeoutMilliseconds = 100;
    private const string Mask = SensitiveMemberNames.Mask;
    private const string BearerReplacement = "Bearer " + Mask;
    private const string SecretReplacement = "${name}" + Mask;

    public static string Apply(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        try
        {
            if (BareDocumentNumber().IsMatch(text))
            {
                return Mask;
            }

            var masked = BearerToken().Replace(text, BearerReplacement);
            masked = JsonWebToken().Replace(masked, Mask);
            masked = ConnectionSecret().Replace(masked, SecretReplacement);
            masked = FormattedCpf().Replace(masked, Mask);

            return FormattedCnpj().Replace(masked, Mask);
        }
        catch (RegexMatchTimeoutException)
        {
            return Mask;
        }
    }

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"(?<name>\b(?:password|pwd|secret|api[_ ]?key|account[_ ]?key)\s*=\s*)[^;\r\n]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex ConnectionSecret();

    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9]{3}\.[0-9]{3}\.[0-9]{3}-[0-9]{2}(?![0-9A-Za-z])", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex FormattedCpf();

    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9A-Z]{2}\.[0-9A-Z]{3}\.[0-9A-Z]{3}/[0-9A-Z]{4}-[0-9]{2}(?![0-9A-Za-z])", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex FormattedCnpj();

    [GeneratedRegex(@"^(?:[0-9]{11}|[0-9]{14})$", RegexOptions.CultureInvariant, TimeoutMilliseconds)]
    private static partial Regex BareDocumentNumber();
}
