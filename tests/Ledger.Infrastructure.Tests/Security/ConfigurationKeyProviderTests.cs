using Ledger.Application.Abstractions;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class ConfigurationKeyProviderTests
{
    private static PiiOptions Configured(int activeVersion, bool withSecondSet)
    {
        var sets = new Dictionary<string, KeySetOptions>
        {
            ["1"] = new()
            {
                EncryptionKey = SecurityVectors.EncryptionKeyBase64,
                BlindIndexKey = SecurityVectors.BlindIndexKeyBase64
            }
        };

        if (withSecondSet)
        {
            sets["2"] = new KeySetOptions
            {
                EncryptionKey = SecurityVectors.SecondEncryptionKeyBase64,
                BlindIndexKey = SecurityVectors.SecondBlindIndexKeyBase64
            };
        }

        return new PiiOptions { Provider = PiiProvider.Configuration, ActiveKeyVersion = activeVersion, KeySets = sets };
    }

    private static ConfigurationKeyProvider Provider(PiiOptions options) =>
        new(Options.Create(options));

    [Fact]
    public void Active_ReturnsTheSetOfTheConfiguredVersion()
    {
        var provider = Provider(Configured(2, withSecondSet: true));

        provider.Active.Version.ShouldBe((ushort)2);
        provider.Active.EncryptionKey.ToArray().ShouldBe(Convert.FromBase64String(SecurityVectors.SecondEncryptionKeyBase64));
        provider.Active.BlindIndexKey.ToArray().ShouldBe(Convert.FromBase64String(SecurityVectors.SecondBlindIndexKeyBase64));
        provider.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void Get_ReturnsEachVersionLoaded()
    {
        var provider = Provider(Configured(2, withSecondSet: true));

        provider.Get(1).Version.ShouldBe((ushort)1);
        provider.Get(2).Version.ShouldBe((ushort)2);
    }

    [Fact]
    public void Get_VersionThatIsNotLoaded_Throws()
    {
        var provider = Provider(Configured(1, withSecondSet: false));

        Should.Throw<KeyNotFoundException>(() => provider.Get(2));
    }

    [Fact]
    public void Live_ListsEveryVersionInAscendingOrder()
    {
        var provider = Provider(Configured(2, withSecondSet: true));

        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1, (ushort)2]);
    }

    [Fact]
    public void Constructor_ActiveVersionWithoutASet_Throws()
    {
        var failure = Should.Throw<KeyMaterialRejectedException>(() => Provider(Configured(3, withSecondSet: true)));

        failure.Problem.ShouldBe(KeyMaterialProblem.NoActiveSet);
        failure.Message.ShouldContain("Security:Pii:ActiveKeyVersion", Case.Sensitive);
    }

    [Fact]
    public void Constructor_MalformedKey_ThrowsWithoutPrintingTheValue()
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Configuration,
            ActiveKeyVersion = 1,
            KeySets = new Dictionary<string, KeySetOptions>
            {
                ["1"] = new()
                {
                    EncryptionKey = Convert.ToBase64String(new byte[31]),
                    BlindIndexKey = SecurityVectors.BlindIndexKeyBase64
                }
            }
        };

        var failure = Should.Throw<KeyMaterialRejectedException>(() => Provider(options));

        failure.Problem.ShouldBe(KeyMaterialProblem.BadLength);
        failure.Message.ShouldContain("Security:Pii:KeySets:1:EncryptionKey", Case.Sensitive);
        failure.Message.ShouldNotContain(SecurityVectors.BlindIndexKeyBase64, Case.Sensitive);
    }

    [Fact]
    public void Options_BoundFromEnvironmentStyleKeys_FeedTheProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Pii:Provider"] = "Configuration",
                ["Security:Pii:ActiveKeyVersion"] = "1",
                ["Security:Pii:KeySets:1:EncryptionKey"] = SecurityVectors.EncryptionKeyBase64,
                ["Security:Pii:KeySets:1:BlindIndexKey"] = SecurityVectors.BlindIndexKeyBase64
            })
            .Build();

        var options = configuration.GetSection(PiiOptions.SectionName).Get<PiiOptions>();

        options.ShouldNotBeNull();
        Provider(options).Active.Version.ShouldBe((ushort)1);
    }
}
