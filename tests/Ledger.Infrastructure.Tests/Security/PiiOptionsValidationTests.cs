using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class PiiOptionsValidationTests
{
    private static readonly PiiOptionsValidator Validator = new();

    private static PiiOptions ValidConfiguration() => new()
    {
        Provider = PiiProvider.Configuration,
        ActiveKeyVersion = 1,
        KeySets = new Dictionary<string, KeySetOptions>
        {
            ["1"] = new()
            {
                EncryptionKey = SecurityVectors.EncryptionKeyBase64,
                BlindIndexKey = SecurityVectors.BlindIndexKeyBase64
            }
        }
    };

    private static PiiOptions WithKeys(string encryptionKey, string blindIndexKey, int activeVersion = 1) => new()
    {
        Provider = PiiProvider.Configuration,
        ActiveKeyVersion = activeVersion,
        KeySets = new Dictionary<string, KeySetOptions>
        {
            ["1"] = new() { EncryptionKey = encryptionKey, BlindIndexKey = blindIndexKey }
        }
    };

    private static ValidateOptionsResult Validate(PiiOptions options) => Validator.Validate(null, options);

    [Fact]
    public void Validate_CompleteConfigurationProvider_Passes()
    {
        Validate(ValidConfiguration()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_DirectoryProviderWithADirectory_Passes()
    {
        var options = new PiiOptions { Provider = PiiProvider.Directory, Directory = "/run/secrets/pii" };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_DirectoryProviderWithoutDirectory_FailsNamingTheKey()
    {
        var result = Validate(new PiiOptions { Provider = PiiProvider.Directory });

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:Directory", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_ActiveKeyVersionOutOfRange_Fails(int version)
    {
        var options = new PiiOptions { Provider = PiiProvider.Directory, Directory = "x", ActiveKeyVersion = version };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:ActiveKeyVersion", Case.Sensitive);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(65535, true)]
    public void Validate_ActiveKeyVersionOnTheEdges_IsAccepted(int version, bool expected)
    {
        var options = new PiiOptions { Provider = PiiProvider.Directory, Directory = "x", ActiveKeyVersion = version };

        Validate(options).Succeeded.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public void Validate_ReloadMinutesOutOfRange_Fails(int minutes)
    {
        var options = new PiiOptions { Provider = PiiProvider.Directory, Directory = "x", ReloadMinutes = minutes };

        Validate(options).Message().ShouldContain("Security:Pii:ReloadMinutes", Case.Sensitive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public void Validate_ReloadMinutesOnTheEdges_IsAccepted(int minutes)
    {
        var options = new PiiOptions { Provider = PiiProvider.Directory, Directory = "x", ReloadMinutes = minutes };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public void Validate_RewrapBatchSizeOutOfRange_Fails(int size)
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Directory,
            Directory = "x",
            Rewrap = new RewrapOptions { BatchSize = size }
        };

        Validate(options).Message().ShouldContain("Security:Pii:Rewrap:BatchSize", Case.Sensitive);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    public void Validate_RewrapBatchSizeOnTheEdges_IsAccepted(int size)
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Directory,
            Directory = "x",
            Rewrap = new RewrapOptions { BatchSize = size }
        };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public void Validate_RewrapIdleSecondsOutOfRange_Fails(int seconds)
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Directory,
            Directory = "x",
            Rewrap = new RewrapOptions { IdleSeconds = seconds }
        };

        Validate(options).Message().ShouldContain("Security:Pii:Rewrap:IdleSeconds", Case.Sensitive);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3600)]
    public void Validate_RewrapIdleSecondsOnTheEdges_IsAccepted(int seconds)
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Directory,
            Directory = "x",
            Rewrap = new RewrapOptions { IdleSeconds = seconds }
        };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Defaults_MatchTheDocumentedValues()
    {
        var options = new PiiOptions();

        options.ReloadMinutes.ShouldBe(10);
        options.Rewrap.BatchSize.ShouldBe(500);
        options.Rewrap.IdleSeconds.ShouldBe(60);
        options.ActiveKeyVersion.ShouldBe(1);
    }

    [Fact]
    public void Validate_KeyOfThirtyOneBytes_FailsNamingTheKeyAndNeverTheValue()
    {
        var short31 = Convert.ToBase64String(Enumerable.Repeat((byte)9, 31).ToArray());

        var result = Validate(WithKeys(short31, SecurityVectors.BlindIndexKeyBase64));

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:KeySets:1:EncryptionKey", Case.Sensitive);
        result.Message().ShouldNotContain(short31, Case.Sensitive);
        result.Message().ShouldNotContain(SecurityVectors.BlindIndexKeyBase64, Case.Sensitive);
    }

    [Fact]
    public void Validate_BlindIndexKeyOfThirtyThreeBytes_FailsNamingTheBlindIndexKey()
    {
        var long33 = Convert.ToBase64String(Enumerable.Repeat((byte)9, 33).ToArray());

        var result = Validate(WithKeys(SecurityVectors.EncryptionKeyBase64, long33));

        result.Message().ShouldContain("Security:Pii:KeySets:1:BlindIndexKey", Case.Sensitive);
        result.Message().ShouldNotContain(long33, Case.Sensitive);
    }

    [Fact]
    public void Validate_TwoIdenticalKeys_FailsNamingBothKeys()
    {
        var result = Validate(WithKeys(SecurityVectors.EncryptionKeyBase64, SecurityVectors.EncryptionKeyBase64));

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:KeySets:1:EncryptionKey", Case.Sensitive);
        result.Message().ShouldContain("Security:Pii:KeySets:1:BlindIndexKey", Case.Sensitive);
        result.Message().ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%%%not-base64%%%")]
    public void Validate_KeyThatIsNotBase64_Fails(string value)
    {
        var result = Validate(WithKeys(value, SecurityVectors.BlindIndexKeyBase64));

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:KeySets:1:EncryptionKey", Case.Sensitive);
        result.Message().ShouldNotContain("%%%not-base64%%%", Case.Sensitive);
    }

    [Fact]
    public void Validate_ActiveVersionWithoutASet_FailsNamingTheActiveVersionKey()
    {
        var result = Validate(WithKeys(
            SecurityVectors.EncryptionKeyBase64,
            SecurityVectors.BlindIndexKeyBase64,
            activeVersion: 2));

        result.Failed.ShouldBeTrue();
        result.Message().ShouldContain("Security:Pii:ActiveKeyVersion", Case.Sensitive);
    }

    [Fact]
    public void Validate_NoKeySetAtAll_FailsForTheConfigurationProvider()
    {
        var options = new PiiOptions { Provider = PiiProvider.Configuration };

        Validate(options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    public void Validate_KeySetNamedWithSomethingThatIsNotAVersion_Fails(string name)
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Configuration,
            KeySets = new Dictionary<string, KeySetOptions>
            {
                [name] = new()
                {
                    EncryptionKey = SecurityVectors.EncryptionKeyBase64,
                    BlindIndexKey = SecurityVectors.BlindIndexKeyBase64
                }
            }
        };

        Validate(options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_SeveralProblems_AreAllReported()
    {
        var options = new PiiOptions
        {
            Provider = PiiProvider.Configuration,
            ReloadMinutes = 0,
            Rewrap = new RewrapOptions { BatchSize = 0 },
            ActiveKeyVersion = 1
        };

        var result = Validate(options);

        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBeGreaterThanOrEqualTo(2);
    }
}
