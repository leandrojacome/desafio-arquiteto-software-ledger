using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ledger.Domain.Entries;

namespace Ledger.Application.Entries;

internal static class CanonicalRequestHash
{
    public const int CurrentVersion = 1;

    private const char Separator = '\n';
    private const string VersionTag = "v1";
    private const string RegisterOperation = "entry.register";
    private const string ReverseOperation = "entry.reverse";
    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

    public static byte[] ForRegistration(RegisterEntryCommand command)
    {
        var canonical = string.Join(
            Separator,
            VersionTag,
            RegisterOperation,
            command.AccountId.ToString(),
            command.ClientId,
            command.Type.ToDatabaseText(),
            command.Amount.ToDecimalString(),
            command.Amount.Currency,
            FormatInstant(command.OccurredAt),
            Normalize(command.Description),
            command.Reference ?? string.Empty);

        return Hash(canonical);
    }

    public static byte[] ForReversal(ReverseEntryCommand command)
    {
        var canonical = string.Join(
            Separator,
            VersionTag,
            ReverseOperation,
            command.AccountId.ToString(),
            command.ClientId,
            command.OriginalEntryId.ToString(),
            Normalize(command.Description));

        return Hash(canonical);
    }

    public static bool Matches(ReadOnlySpan<byte> stored, ReadOnlySpan<byte> computed) =>
        CryptographicOperations.FixedTimeEquals(stored, computed);

    private static byte[] Hash(string canonical) => SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

    private static string Normalize(string? description) => description?.Trim() ?? string.Empty;

    private static string FormatInstant(DateTimeOffset? instant) =>
        instant is { } value ? value.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture) : string.Empty;
}
