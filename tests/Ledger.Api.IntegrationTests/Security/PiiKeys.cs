using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Security;

internal static class PiiKeys
{
    private static readonly string ThirdEncryptionKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x33, 32).ToArray());
    private static readonly string ThirdBlindIndexKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x34, 32).ToArray());

    public static PiiOptions Configured(int activeVersion, params int[] versions)
    {
        var sets = new Dictionary<string, KeySetOptions>();

        foreach (var version in versions)
        {
            sets[version.ToString(System.Globalization.CultureInfo.InvariantCulture)] = version switch
            {
                1 => new KeySetOptions
                {
                    EncryptionKey = TestConfiguration.PiiEncryptionKeyOne,
                    BlindIndexKey = TestConfiguration.PiiBlindIndexKeyOne
                },
                2 => new KeySetOptions
                {
                    EncryptionKey = TestConfiguration.PiiEncryptionKeyTwo,
                    BlindIndexKey = TestConfiguration.PiiBlindIndexKeyTwo
                },
                3 => new KeySetOptions { EncryptionKey = ThirdEncryptionKey, BlindIndexKey = ThirdBlindIndexKey },
                _ => throw new ArgumentOutOfRangeException(nameof(versions), version, "Only versions 1 to 3 have test keys.")
            };
        }

        return new PiiOptions
        {
            Provider = PiiProvider.Configuration,
            ActiveKeyVersion = activeVersion,
            KeySets = sets
        };
    }

    public static ConfigurationKeyProvider Provider(int activeVersion, params int[] versions) =>
        new(Options.Create(Configured(activeVersion, versions)));

    public static HolderDocumentProtector Protector(int activeVersion, params int[] versions) =>
        new(Provider(activeVersion, versions), new AesGcmDocumentCipher());
}
