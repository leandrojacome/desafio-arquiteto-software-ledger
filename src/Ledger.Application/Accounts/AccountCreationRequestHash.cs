using System.Security.Cryptography;
using System.Text;
using Ledger.Domain.Shared;

namespace Ledger.Application.Accounts;

internal static class AccountCreationRequestHash
{
    public const int CurrentVersion = 1;

    private const char Separator = '\n';
    private const string VersionTag = "v1";
    private const string Operation = "account.create";

    public static byte[] Compute(string currency, Money overdraftLimit, ReadOnlySpan<byte> holderBlindIndex)
    {
        var canonical = string.Join(
            Separator,
            VersionTag,
            Operation,
            currency,
            overdraftLimit.ToDecimalString(),
            Convert.ToHexStringLower(holderBlindIndex));

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    public static bool Matches(ReadOnlySpan<byte> stored, ReadOnlySpan<byte> computed) =>
        CryptographicOperations.FixedTimeEquals(stored, computed);
}
