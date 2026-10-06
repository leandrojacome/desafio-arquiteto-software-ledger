using Ledger.Application.Security;
using Ledger.Domain.Accounts;

namespace Ledger.Application.Tests.Security;

internal static class SecurityVectors
{
    public const string EncryptionKeyBase64 = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    public const string BlindIndexKeyBase64 = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=";
    public const string SecondEncryptionKeyBase64 = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU=";
    public const string SecondBlindIndexKeyBase64 = "enl4d3Z1dHNycXBvbm1sa2ppaGdmZWRjYmEwOTg3NjU=";
    public const string NonceHex = "000102030405060708090a0b";
    public const string AccountText = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";
    public const string OtherAccountText = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d34";
    public const string AssociatedDataHex = "0100010192b7c281aa7e04b1d56f0c2a9e8d33";

    public const string CpfDocument = "12345678909";
    public const string CnpjNumericDocument = "12345678000195";
    public const string CnpjAlphanumericDocument = "12ABC34501DE35";

    public const string CpfBlobHex = "010001000102030405060708090a0b1cd78848ec2ee59491ec48c14ff5917bfebdfeffcee14de692c9a4";
    public const string CpfBlobBase64 = "AQABAAECAwQFBgcICQoLHNeISOwu5ZSR7EjBT/WRe/69/v/O4U3mksmk";
    public const string CnpjBlobHex = "010001000102030405060708090a0b1cd7fa3e9a2be69998ed35fb42fa71283c3b33c3f712104208a9f8c2bf19";
    public const string CnpjBlobBase64 = "AQABAAECAwQFBgcICQoLHNf6Ppor5pmY7TX7QvpxKDw7M8P3EhBCCKn4wr8Z";

    public const string CpfIndexHex = "3ce1a1b4e9568a5937857e103db87f0b2f06250756939bd56d7a182607f1c21b";
    public const string CnpjNumericIndexHex = "06826c47b479b9383835bb752fa530efce9fcd935405fbebf3ebde6e8a245a46";
    public const string CnpjAlphanumericIndexHex = "169ee425f44d346f1848904c183b1b65180ec79c82b0e624fc6b744e069cd7ad";

    public static AccountId Account => AccountId.From(Guid.Parse(AccountText)).Value;

    public static AccountId OtherAccount => AccountId.From(Guid.Parse(OtherAccountText)).Value;

    public static KeySet KeySetOne => Keys(1, EncryptionKeyBase64, BlindIndexKeyBase64);

    public static KeySet KeySetTwo => Keys(2, SecondEncryptionKeyBase64, SecondBlindIndexKeyBase64);

    public static KeySet Keys(ushort version, string encryptionKeyBase64, string blindIndexKeyBase64)
    {
        return new KeySet(
            version,
            Convert.FromBase64String(encryptionKeyBase64),
            Convert.FromBase64String(blindIndexKeyBase64));
    }

    public static byte[] Nonce() => Convert.FromHexString(NonceHex);
}
