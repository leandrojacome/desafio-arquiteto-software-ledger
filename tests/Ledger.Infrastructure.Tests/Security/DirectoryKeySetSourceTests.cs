using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class DirectoryKeySetSourceTests
{
    private static DirectoryKeySetSource Source(string? directory) =>
        new(Options.Create(new PiiOptions { Provider = PiiProvider.Directory, Directory = directory }));

    [Fact]
    public void Load_ReadsEveryVersionFolder()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne().WithDefaultVersionTwo();

        var sets = Source(directory.Root).Load();

        sets.Select(set => set.Version).Order().ShouldBe([(ushort)1, (ushort)2]);
        sets.Single(set => set.Version == 2).EncryptionKey.Trim().ShouldBe(SecurityVectors.SecondEncryptionKeyBase64);
        sets.Single(set => set.Version == 1).BlindIndexKey.Trim().ShouldBe(SecurityVectors.BlindIndexKeyBase64);
    }

    [Fact]
    public void Load_IgnoresFoldersWhoseNameIsNotAVersion()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();
        Directory.CreateDirectory(Path.Combine(directory.Root, "..data"));
        Directory.CreateDirectory(Path.Combine(directory.Root, "backup"));
        Directory.CreateDirectory(Path.Combine(directory.Root, "0"));

        var sets = Source(directory.Root).Load();

        sets.Select(set => set.Version).ShouldBe([(ushort)1]);
    }

    [Fact]
    public void Load_TwoFoldersThatNameTheSameVersion_SurfaceAsARejectionNotAnArgumentError()
    {
        using var directory = new KeyDirectory()
            .WithFolder("7", SecurityVectors.EncryptionKeyBase64, SecurityVectors.BlindIndexKeyBase64)
            .WithFolder("07", SecurityVectors.SecondEncryptionKeyBase64, SecurityVectors.SecondBlindIndexKeyBase64);
        var sets = Source(directory.Root).Load();

        var failure = Should.Throw<KeyMaterialRejectedException>(() => KeySetReader.BuildSnapshot(sets, 7, "active"));

        failure.Problem.ShouldBe(KeyMaterialProblem.DuplicateVersion);
        failure.Message.ShouldContain("7");
    }

    [Fact]
    public void Load_TrailingNewlinesAreLeftToTheReader()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();

        var raw = Source(directory.Root).Load().Single();

        KeySetReader.Read(raw).Version.ShouldBe((ushort)1);
    }

    [Fact]
    public void Load_MissingFolder_IsUnavailable()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ledger-keys-missing-" + Guid.NewGuid().ToString("N"));

        Should.Throw<KeySourceUnavailableException>(() => Source(missing).Load());
    }

    [Fact]
    public void Load_EmptyFolder_IsUnavailable()
    {
        using var directory = new KeyDirectory();

        Should.Throw<KeySourceUnavailableException>(() => Source(directory.Root).Load());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Load_DirectoryNotConfigured_IsUnavailable(string? configured)
    {
        Should.Throw<KeySourceUnavailableException>(() => Source(configured).Load());
    }

    [Theory]
    [InlineData("encryption.key")]
    [InlineData("blind-index.key")]
    public void Load_VersionFolderMissingAFile_IsUnavailableAndKeepsTheIoErrorAsCause(string missingFile)
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();
        directory.DeleteFile(1, missingFile);

        var failure = Should.Throw<KeySourceUnavailableException>(() => Source(directory.Root).Load());

        failure.InnerException.ShouldBeAssignableTo<IOException>();
    }

    [Fact]
    public void Load_MalformedContentIsNotRejectedByTheSourceItself()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();
        directory.Overwrite(1, "encryption.key", "not-base64-at-all");

        var raw = Source(directory.Root).Load().Single();

        Should.Throw<KeyMaterialRejectedException>(() => KeySetReader.Read(raw))
            .Problem.ShouldBe(KeyMaterialProblem.BadBase64);
    }

    [Fact]
    public void Failure_MessageNamesNoKeyMaterial()
    {
        using var directory = new KeyDirectory().WithDefaultVersionOne();
        directory.DeleteFile(1, "blind-index.key");

        var failure = Should.Throw<KeySourceUnavailableException>(() => Source(directory.Root).Load());

        failure.Message.ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
        failure.ToString().ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
    }
}
