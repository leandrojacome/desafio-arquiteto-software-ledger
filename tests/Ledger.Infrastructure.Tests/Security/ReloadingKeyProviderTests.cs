using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Application.Tests.Support;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class ReloadingKeyProviderTests
{
    private static readonly TimeSpan ReloadPeriod = TimeSpan.FromMinutes(10);

    private FakeTimeProvider Time { get; } = new();

    private ISecurityTelemetry Telemetry { get; } = Substitute.For<ISecurityTelemetry>();

    private CapturingLogger<ReloadingKeyProvider> Logger { get; } = new();

    private ScriptedSource Source { get; } = new();

    private ReloadingKeyProvider Provider(
        int activeVersion = 1,
        Func<KeySet, bool>? roundTrip = null)
    {
        var options = Options.Create(new PiiOptions
        {
            Provider = PiiProvider.Directory,
            ActiveKeyVersion = activeVersion,
            ReloadMinutes = 10,
            Directory = "unused"
        });

        return new ReloadingKeyProvider(Source, options, Time, Telemetry, Logger, roundTrip ?? (_ => true));
    }

    private static RawKeySet Raw(int version, string encryption, string blindIndex) =>
        new((ushort)version, encryption, blindIndex, $"keys/{version}/encryption.key", $"keys/{version}/blind-index.key");

    private static RawKeySet SetOne() => Raw(1, SecurityVectors.EncryptionKeyBase64, SecurityVectors.BlindIndexKeyBase64);

    private static RawKeySet SetTwo() =>
        Raw(2, SecurityVectors.SecondEncryptionKeyBase64, SecurityVectors.SecondBlindIndexKeyBase64);

    [Fact]
    public void Constructor_SourceDeliversTheActiveSet_IsAvailable()
    {
        Source.Returns(SetOne());

        using var provider = Provider();

        provider.IsAvailable.ShouldBeTrue();
        provider.Active.Version.ShouldBe((ushort)1);
        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1]);
        provider.Get(1).Version.ShouldBe((ushort)1);
    }

    [Fact]
    public void Constructor_UnreachableSource_StartsWithoutKeysAndDoesNotThrow()
    {
        Source.Unreachable();

        using var provider = Provider();

        provider.IsAvailable.ShouldBeFalse();
        provider.Live.ShouldBeEmpty();
        Should.Throw<KeyProviderUnavailableException>(() => provider.Active);
        Should.Throw<KeyProviderUnavailableException>(() => provider.Get(1));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_KeyOfTheWrongSize_Throws(int size)
    {
        Source.Returns(Raw(1, Convert.ToBase64String(new byte[size]), SecurityVectors.BlindIndexKeyBase64));

        var failure = Should.Throw<KeyMaterialRejectedException>(() => Provider());

        failure.Problem.ShouldBe(KeyMaterialProblem.BadLength);
        Logger.Single(7005).Level.ShouldBe(LogLevel.Error);
    }

    [Fact]
    public void Constructor_IdenticalKeys_Throws()
    {
        Source.Returns(Raw(1, SecurityVectors.EncryptionKeyBase64, SecurityVectors.EncryptionKeyBase64));

        Should.Throw<KeyMaterialRejectedException>(() => Provider()).Problem.ShouldBe(KeyMaterialProblem.IdenticalKeys);
    }

    [Fact]
    public void Constructor_InvalidBase64_Throws()
    {
        Source.Returns(Raw(1, "***", SecurityVectors.BlindIndexKeyBase64));

        Should.Throw<KeyMaterialRejectedException>(() => Provider()).Problem.ShouldBe(KeyMaterialProblem.BadBase64);
    }

    [Fact]
    public void Constructor_ActiveVersionWithoutASet_Throws()
    {
        Source.Returns(SetOne());

        Should.Throw<KeyMaterialRejectedException>(() => Provider(activeVersion: 2))
            .Problem.ShouldBe(KeyMaterialProblem.NoActiveSet);
    }

    [Fact]
    public void Constructor_RoundTripFails_Throws()
    {
        Source.Returns(SetOne());

        Should.Throw<KeyMaterialRejectedException>(() => Provider(roundTrip: _ => false))
            .Problem.ShouldBe(KeyMaterialProblem.RoundTripFailed);
    }

    [Fact]
    public void Reload_AfterTheConfiguredPeriod_SwapsTheWholeSnapshotAtOnce()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        var before = provider.Live;
        Source.Returns(SetOne(), SetTwo());

        Time.Advance(ReloadPeriod);

        before.Select(set => set.Version).ShouldBe([(ushort)1]);
        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1, (ushort)2]);
        Telemetry.Received(1).KeyReloaded(true);
        var log = Logger.Single(7004);
        log.Level.ShouldBe(LogLevel.Information);
        log.Properties["Versions"].ShouldBe("1,2");
        log.Properties["ActiveVersion"].ShouldBe("1");
    }

    [Fact]
    public void Reload_BeforeThePeriod_DoesNotReadTheSourceAgain()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        var readsAfterStartup = Source.Reads;

        Time.Advance(ReloadPeriod - TimeSpan.FromSeconds(1));

        Source.Reads.ShouldBe(readsAfterStartup);
        provider.IsAvailable.ShouldBeTrue();
    }

    [Fact]
    public void Reload_EveryPeriod_ReadsTheSourceAgain()
    {
        Source.Returns(SetOne());
        using var provider = Provider();

        Time.Advance(ReloadPeriod);
        Time.Advance(ReloadPeriod);
        Time.Advance(ReloadPeriod);

        Source.Reads.ShouldBe(4);
        Telemetry.Received(3).KeyReloaded(true);
    }

    [Fact]
    public void Reload_UnreachableSource_KeepsThePreviousSnapshotAndRecordsTheFailure()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Unreachable();

        Time.Advance(ReloadPeriod);

        provider.IsAvailable.ShouldBeTrue();
        provider.Active.Version.ShouldBe((ushort)1);
        Telemetry.Received(1).KeyReloaded(false);
        var log = Logger.Single(7003);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["ExceptionType"].ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Reload_MalformedMaterial_DiscardsTheNewSnapshotAndKeepsThePreviousOne()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Returns(Raw(1, Convert.ToBase64String(new byte[31]), SecurityVectors.BlindIndexKeyBase64));

        Time.Advance(ReloadPeriod);

        provider.Active.EncryptionKey.ToArray().ShouldBe(Convert.FromBase64String(SecurityVectors.EncryptionKeyBase64));
        Telemetry.Received(1).KeyReloaded(false);
        var log = Logger.Single(7005);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["Reason"].ShouldBe("bad_length");
    }

    [Fact]
    public void Reload_ActiveVersionRemovedFromTheSource_KeepsThePreviousSnapshot()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Returns(SetTwo());

        Time.Advance(ReloadPeriod);

        provider.Active.Version.ShouldBe((ushort)1);
        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1]);
        Logger.Single(7005).Properties["Reason"].ShouldBe("no_active_set");
    }

    [Fact]
    public void Reload_ExistingVersionWithOtherKeyMaterial_IsRejectedAndThePreviousSnapshotStays()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Returns(Raw(1, SecurityVectors.SecondEncryptionKeyBase64, SecurityVectors.SecondBlindIndexKeyBase64));

        Time.Advance(ReloadPeriod);

        provider.Active.EncryptionKey.ToArray().ShouldBe(Convert.FromBase64String(SecurityVectors.EncryptionKeyBase64));
        provider.Active.BlindIndexKey.ToArray().ShouldBe(Convert.FromBase64String(SecurityVectors.BlindIndexKeyBase64));
        Telemetry.Received(1).KeyReloaded(false);
        var log = Logger.Single(7005);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["Reason"].ShouldBe("version_changed");
        provider.VanishedVersions.ShouldBeEmpty();
    }

    [Fact]
    public void Reload_NewVersionAlongsideTheSameOldKeyMaterial_IsAdopted()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Returns(SetOne(), SetTwo());

        Time.Advance(ReloadPeriod);

        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1, (ushort)2]);
        Logger.Contains(7005).ShouldBeFalse();
        Logger.Contains(7009).ShouldBeFalse();
    }

    [Fact]
    public void Reload_VersionThatVanishesFromTheSource_IsAdoptedWarnedAndReported()
    {
        Source.Returns(SetOne(), SetTwo());
        using var provider = Provider(activeVersion: 2);
        provider.VanishedVersions.ShouldBeEmpty();
        Source.Returns(SetTwo());

        Time.Advance(ReloadPeriod);

        provider.Live.Select(set => set.Version).ShouldBe([(ushort)2]);
        provider.VanishedVersions.ShouldBe([(ushort)1]);
        Telemetry.Received(1).KeyReloaded(true);
        var log = Logger.Single(7009);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Versions"].ShouldBe("1");
    }

    [Fact]
    public void Reload_VanishedVersionAlreadyReported_IsNotWarnedAgain()
    {
        Source.Returns(SetOne(), SetTwo());
        using var provider = Provider(activeVersion: 2);
        Source.Returns(SetTwo());

        Time.Advance(ReloadPeriod);
        Time.Advance(ReloadPeriod);

        provider.VanishedVersions.ShouldBe([(ushort)1]);
        Logger.Entries.Count(entry => entry.EventId.Id == 7009).ShouldBe(1);
    }

    [Fact]
    public void Reload_VanishedVersionThatComesBack_StopsBeingReported()
    {
        Source.Returns(SetOne(), SetTwo());
        using var provider = Provider(activeVersion: 2);
        Source.Returns(SetTwo());
        Time.Advance(ReloadPeriod);
        Source.Returns(SetOne(), SetTwo());

        Time.Advance(ReloadPeriod);

        provider.VanishedVersions.ShouldBeEmpty();
        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1, (ushort)2]);
    }

    [Fact]
    public void Constructor_TwoSetsForTheSameVersion_Throws()
    {
        Source.Returns(SetOne(), SetOne());

        Should.Throw<KeyMaterialRejectedException>(() => Provider())
            .Problem.ShouldBe(KeyMaterialProblem.DuplicateVersion);
        Logger.Single(7005).Properties["Reason"].ShouldBe("duplicate_version");
    }

    [Fact]
    public void Reload_TwoSetsForTheSameVersion_KeepsThePreviousSnapshot()
    {
        Source.Returns(SetOne());
        using var provider = Provider();
        Source.Returns(SetOne(), SetOne());

        Time.Advance(ReloadPeriod);

        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1]);
        Telemetry.Received(1).KeyReloaded(false);
        Logger.Single(7005).Properties["Reason"].ShouldBe("duplicate_version");
    }

    [Fact]
    public void Reload_RoundTripFailsOnTheNewSnapshot_KeepsThePreviousOne()
    {
        Source.Returns(SetOne());
        var calls = 0;
        using var provider = Provider(roundTrip: _ => ++calls == 1);
        Source.Returns(SetOne(), SetTwo());

        Time.Advance(ReloadPeriod);

        provider.Live.Select(set => set.Version).ShouldBe([(ushort)1]);
        Logger.Single(7005).Properties["Reason"].ShouldBe("round_trip_failed");
        Telemetry.Received(1).KeyReloaded(false);
    }

    [Fact]
    public void Reload_SourceComesBackAfterAnUnavailableStartup_BecomesAvailable()
    {
        Source.Unreachable();
        using var provider = Provider();
        Source.Returns(SetOne());

        Time.Advance(ReloadPeriod);

        provider.IsAvailable.ShouldBeTrue();
        provider.Active.Version.ShouldBe((ushort)1);
        Telemetry.Received(1).KeyReloaded(true);
    }

    [Fact]
    public void Dispose_StopsTheReloadTimer()
    {
        Source.Returns(SetOne());
        var provider = Provider();
        var readsAfterStartup = Source.Reads;

        provider.Dispose();
        Time.Advance(ReloadPeriod * 3);

        Source.Reads.ShouldBe(readsAfterStartup);
    }

    [Fact]
    public void Everything_NeverPrintsKeyMaterialInLogsOrExceptions()
    {
        Source.Returns(Raw(1, Convert.ToBase64String(new byte[31]), SecurityVectors.BlindIndexKeyBase64));
        var secrets = new[]
        {
            SecurityVectors.BlindIndexKeyBase64,
            Convert.ToBase64String(new byte[31]),
            Convert.ToHexString(Convert.FromBase64String(SecurityVectors.BlindIndexKeyBase64))
        };

        var failure = Should.Throw<KeyMaterialRejectedException>(() => Provider());

        foreach (var secret in secrets)
        {
            failure.ToString().ShouldNotContain(secret, Case.Insensitive);
            Logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void KeySet_ToString_PrintsOnlyTheVersion()
    {
        Source.Returns(SetOne());
        using var provider = Provider();

        var text = provider.Active.ToString();

        text.ShouldContain("Version = 1", Case.Sensitive);
        text.ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
        text.ShouldNotContain("System.Byte", Case.Sensitive);
        text.ShouldNotContain("ReadOnlyMemory", Case.Sensitive);
    }

    private sealed class ScriptedSource : IKeySetSource
    {
        private RawKeySet[]? _sets = [];

        public int Reads { get; private set; }

        public void Returns(params RawKeySet[] sets) => _sets = sets;

        public void Unreachable() => _sets = null;

        public IReadOnlyList<RawKeySet> Load()
        {
            Reads++;

            return _sets ?? throw new KeySourceUnavailableException("The scripted source is unreachable.");
        }
    }
}
