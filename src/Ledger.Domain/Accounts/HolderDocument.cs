using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

public readonly record struct HolderDocument
{
    private const int MaxRawLength = 18;
    private const int CpfLength = 11;
    private const int CnpjLength = 14;
    private const int CpfFirstCheckIndex = 9;
    private const int CpfSecondCheckIndex = 10;
    private const int CnpjFirstCheckIndex = 12;
    private const int CnpjSecondCheckIndex = 13;
    private const int CpfMaskStart = 6;
    private const int CpfMaskLength = 3;
    private const int CnpjMaskStart = 8;
    private const int CnpjMaskLength = 4;

    private readonly string? _normalized;

    private HolderDocument(string normalized)
    {
        _normalized = normalized;
    }

    public string Normalized => _normalized ?? throw NotCreated();

    public HolderDocumentKind Kind => Normalized.Length == CpfLength ? HolderDocumentKind.Cpf : HolderDocumentKind.Cnpj;

    private static ReadOnlySpan<int> FirstCnpjWeights => [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    private static ReadOnlySpan<int> SecondCnpjWeights => [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static Result<HolderDocument> From(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxRawLength)
        {
            return AccountErrors.InvalidHolderDocument;
        }

        var normalized = Normalize(raw);

        return normalized.Length switch
        {
            CpfLength when IsValidCpf(normalized) => new HolderDocument(normalized),
            CnpjLength when IsValidCnpj(normalized) => new HolderDocument(normalized),
            _ => AccountErrors.InvalidHolderDocument
        };
    }

    public string Masked() => Kind == HolderDocumentKind.Cpf
        ? $"***.***.{Normalized.AsSpan(CpfMaskStart, CpfMaskLength)}-**"
        : $"**.***.***/{Normalized.AsSpan(CnpjMaskStart, CnpjMaskLength)}-**";

    public override string ToString() => _normalized is null ? string.Empty : Masked();

    private static InvalidOperationException NotCreated() =>
        new("The holder document was not created by HolderDocument.From.");

    private static string Normalize(string raw)
    {
        Span<char> buffer = stackalloc char[MaxRawLength];
        var length = 0;

        foreach (var character in raw)
        {
            if (character is '.' or '-' or '/')
            {
                continue;
            }

            buffer[length] = char.IsAsciiLetterLower(character) ? char.ToUpperInvariant(character) : character;
            length++;
        }

        return new string(buffer[..length]);
    }

    private static bool IsValidCpf(string normalized)
    {
        if (!AreAsciiDigits(normalized) || IsSingleRepeatedCharacter(normalized))
        {
            return false;
        }

        return CpfCheckDigit(normalized.AsSpan(0, CpfFirstCheckIndex)) == DigitAt(normalized, CpfFirstCheckIndex)
            && CpfCheckDigit(normalized.AsSpan(0, CpfSecondCheckIndex)) == DigitAt(normalized, CpfSecondCheckIndex);
    }

    private static bool IsValidCnpj(string normalized)
    {
        var body = normalized.AsSpan(0, CnpjFirstCheckIndex);
        var checkDigits = normalized.AsSpan(CnpjFirstCheckIndex);

        if (!AreAsciiDigitsOrUppercaseLetters(body) || !AreAsciiDigits(checkDigits)
            || IsSingleRepeatedCharacter(normalized))
        {
            return false;
        }

        return CnpjCheckDigit(body, FirstCnpjWeights) == DigitAt(normalized, CnpjFirstCheckIndex)
            && CnpjCheckDigit(normalized.AsSpan(0, CnpjSecondCheckIndex), SecondCnpjWeights)
                == DigitAt(normalized, CnpjSecondCheckIndex);
    }

    private static bool AreAsciiDigits(ReadOnlySpan<char> characters)
    {
        foreach (var character in characters)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreAsciiDigitsOrUppercaseLetters(ReadOnlySpan<char> characters)
    {
        foreach (var character in characters)
        {
            if (!char.IsAsciiDigit(character) && !char.IsAsciiLetterUpper(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSingleRepeatedCharacter(string text) => text.All(character => character == text[0]);

    private static int DigitAt(string text, int index) => text[index] - '0';

    private static int CpfCheckDigit(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var weight = digits.Length + 1;

        foreach (var digit in digits)
        {
            sum += (digit - '0') * weight;
            weight--;
        }

        var remainder = sum * 10 % 11;

        return remainder == 10 ? 0 : remainder;
    }

    private static int CnpjCheckDigit(ReadOnlySpan<char> characters, ReadOnlySpan<int> weights)
    {
        var sum = 0;

        for (var index = 0; index < characters.Length; index++)
        {
            sum += (characters[index] - '0') * weights[index];
        }

        var remainder = sum % 11;

        return remainder < 2 ? 0 : 11 - remainder;
    }
}
