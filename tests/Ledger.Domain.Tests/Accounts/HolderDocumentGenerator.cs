using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Ledger.Domain.Tests.Accounts;

internal static class HolderDocumentGenerator
{
    private const string AlphanumericAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [SuppressMessage("Security", "CA5394",
        Justification = "The callers pass a seeded generator so the documents are reproducible; no security decision depends on it.")]
    public static string Cpf(Random random)
    {
        int[] digits;
        do
        {
            digits = new int[11];
            for (var index = 0; index < 9; index++)
            {
                digits[index] = random.Next(10);
            }
        }
        while (digits.Take(9).Distinct().Count() == 1);

        digits[9] = CpfCheck(digits, 9);
        digits[10] = CpfCheck(digits, 10);

        return string.Concat(digits.Select(digit => (char)('0' + digit)));
    }

    [SuppressMessage("Security", "CA5394",
        Justification = "The callers pass a seeded generator so the documents are reproducible; no security decision depends on it.")]
    public static string Cnpj(Random random, bool alphanumeric)
    {
        var alphabet = alphanumeric ? AlphanumericAlphabet : AlphanumericAlphabet[..10];
        string baseText;
        do
        {
            var builder = new StringBuilder();
            for (var index = 0; index < 12; index++)
            {
                builder.Append(alphabet[random.Next(alphabet.Length)]);
            }

            baseText = builder.ToString();
        }
        while (baseText.Distinct().Count() == 1);

        var first = CnpjCheck(baseText);
        var second = CnpjCheck(baseText + first);

        return baseText + first + second;
    }

    private static int CpfCheck(int[] digits, int count)
    {
        var sum = 0;
        for (var offset = 0; offset < count; offset++)
        {
            sum += digits[count - 1 - offset] * (offset + 2);
        }

        var result = 11 - (sum % 11);

        return result >= 10 ? 0 : result;
    }

    private static int CnpjCheck(string text)
    {
        var sum = 0;
        for (var offset = 0; offset < text.Length; offset++)
        {
            sum += (text[text.Length - 1 - offset] - '0') * (2 + (offset % 8));
        }

        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }

    public static string Format(string normalized)
    {
        return normalized.Length == 11
            ? $"{normalized[..3]}.{normalized[3..6]}.{normalized[6..9]}-{normalized[9..]}"
            : $"{normalized[..2]}.{normalized[2..5]}.{normalized[5..8]}/{normalized[8..12]}-{normalized[12..]}";
    }
}
