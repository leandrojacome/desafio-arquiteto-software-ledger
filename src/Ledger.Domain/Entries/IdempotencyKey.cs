using System.Security.Cryptography;
using System.Text;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public readonly record struct IdempotencyKey
{
    public const int MaxLength = 128;

    private const int FingerprintBytes = 4;

    private readonly string? _value;

    private IdempotencyKey(string value)
    {
        _value = value;
    }

    public string Value => _value ?? throw new InvalidOperationException(
        "The idempotency key was not created by IdempotencyKey.From.");

    public string Fingerprint
    {
        get
        {
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(Encoding.ASCII.GetBytes(Value), hash);

            return Convert.ToHexStringLower(hash[..FingerprintBytes]);
        }
    }

    public static Result<IdempotencyKey> From(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return EntryErrors.IdempotencyKeyRequired;
        }

        if (text.Length > MaxLength || !VisibleAscii.ContainsOnlyVisible(text))
        {
            return EntryErrors.IdempotencyKeyMalformed;
        }

        return new IdempotencyKey(text);
    }

    public override string ToString() => _value is null ? string.Empty : Fingerprint;
}
