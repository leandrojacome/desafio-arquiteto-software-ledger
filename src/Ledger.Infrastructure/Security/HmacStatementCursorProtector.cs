using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Ledger.Application.Abstractions;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Infrastructure.Security;

internal sealed class HmacStatementCursorProtector : IStatementCursorProtector
{
    public const int KeySize = 32;
    public const int EncodedLength = 44;
    public const byte FormatVersion = 1;

    private const int GuidSize = 16;
    private const int PayloadSize = 1 + sizeof(long) + sizeof(long);
    private const int SignatureSize = 16;
    private const int CursorSize = PayloadSize + SignatureSize;
    private const long TicksPerMicrosecond = 10;
    private const long MinMicroseconds = -62_135_596_800_000_000;
    private const long MaxMicroseconds = 253_402_300_799_999_999;

    private static readonly long EpochTicks = DateTimeOffset.UnixEpoch.UtcTicks;

    private readonly byte[] _signingKey;

    private HmacStatementCursorProtector(byte[] signingKey)
    {
        _signingKey = signingKey;
    }

    public static HmacStatementCursorProtector Create(ReadOnlySpan<byte> signingKey)
    {
        if (signingKey.Length != KeySize)
        {
            throw new ArgumentException($"The signing key must have exactly {KeySize} bytes.", nameof(signingKey));
        }

        return new HmacStatementCursorProtector(signingKey.ToArray());
    }

    public string Protect(AccountId accountId, StatementPosition position)
    {
        if (position.AccountVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "The position of a cursor must be greater than zero.");
        }

        Span<byte> cursor = stackalloc byte[CursorSize];
        cursor[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(cursor.Slice(1, sizeof(long)), ToMicroseconds(position.RecordedAt));
        BinaryPrimitives.WriteInt64BigEndian(cursor.Slice(1 + sizeof(long), sizeof(long)), position.AccountVersion);

        Sign(accountId, cursor[..PayloadSize], cursor[PayloadSize..]);

        return Base64Url.EncodeToString(cursor);
    }

    public Result<StatementPosition> Unprotect(AccountId accountId, string cursor)
    {
        if (!TryDecode(cursor, out var bytes))
        {
            return StatementErrors.InvalidCursor;
        }

        Span<byte> expected = stackalloc byte[SignatureSize];
        Sign(accountId, bytes[..PayloadSize], expected);

        var signatureMatches = CryptographicOperations.FixedTimeEquals(expected, bytes[PayloadSize..]);

        if (!signatureMatches || bytes[0] != FormatVersion)
        {
            return StatementErrors.InvalidCursor;
        }

        var microseconds = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(1, sizeof(long)));
        var accountVersion = BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(1 + sizeof(long), sizeof(long)));

        if (accountVersion <= 0 || microseconds is < MinMicroseconds or > MaxMicroseconds)
        {
            return StatementErrors.InvalidCursor;
        }

        return new StatementPosition(FromMicroseconds(microseconds), accountVersion);
    }

    private static long ToMicroseconds(DateTimeOffset instant)
    {
        var ticks = instant.UtcTicks - EpochTicks;

        if (ticks % TicksPerMicrosecond != 0)
        {
            throw new ArgumentException(
                "The recorded instant of a cursor must have microsecond precision.",
                nameof(instant));
        }

        return ticks / TicksPerMicrosecond;
    }

    private static DateTimeOffset FromMicroseconds(long microseconds)
    {
        return new DateTimeOffset(EpochTicks + (microseconds * TicksPerMicrosecond), TimeSpan.Zero);
    }

    private static bool TryDecode(string cursor, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;

        if (cursor.Length != EncodedLength || !IsBase64UrlAlphabet(cursor))
        {
            return false;
        }

        var buffer = new byte[CursorSize];

        if (!Base64Url.TryDecodeFromChars(cursor, buffer, out var written) || written != CursorSize)
        {
            return false;
        }

        bytes = buffer;

        return true;
    }

    private static bool IsBase64UrlAlphabet(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private void Sign(AccountId accountId, ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        Span<byte> input = stackalloc byte[GuidSize + payload.Length];

        if (!accountId.Value.TryWriteBytes(input, bigEndian: true, out _))
        {
            throw new InvalidOperationException("The signature buffer is too small for the account id.");
        }

        payload.CopyTo(input[GuidSize..]);

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_signingKey, input, hash);
        hash[..destination.Length].CopyTo(destination);
    }
}
