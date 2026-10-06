using Ledger.Application.Abstractions;
using Ledger.Application.Security;
using Ledger.Application.Tests.Security;
using Ledger.Application.Tests.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ledger.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public sealed class KeyProviderStartupGuardTests
{
    private CapturingLogger<KeyProviderStartupGuard> Logger { get; } = new();

    private static IHostEnvironment HostEnvironment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);

        return environment;
    }

    private KeyProviderStartupGuard Guard(
        IKeyProvider provider,
        IHolderDocumentProtector protector,
        PiiProvider piiProvider,
        string environment)
    {
        return new KeyProviderStartupGuard(
            provider,
            protector,
            Options.Create(new PiiOptions { Provider = piiProvider }),
            HostEnvironment(environment),
            Logger);
    }

    private static HolderDocumentProtector RealProtector(IKeyProvider provider) =>
        new(provider, new AesGcmDocumentCipher());

    [Fact]
    public async Task StartAsync_ConfigurationProviderInProduction_RefusesToStart()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Configuration, Environments.Production);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));

        failure.Message.ShouldContain("Security:Pii:Provider", Case.Sensitive);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task StartAsync_ConfigurationProviderInADevelopmentOrTestEnvironment_Starts(string environment)
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Configuration, environment);

        await Should.NotThrowAsync(() => guard.StartAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    [InlineData("")]
    public async Task StartAsync_ConfigurationProviderInAnyOtherEnvironment_RefusesToStart(string environment)
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Configuration, environment);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));

        failure.Message.ShouldContain("Security:Pii:Provider", Case.Sensitive);
    }

    [Fact]
    public async Task StartAsync_DirectoryProviderInProduction_Starts()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Directory, Environments.Production);

        await Should.NotThrowAsync(() => guard.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_ProviderUnavailable_StartsAndLogsTheStartupWarning()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne).WentDown();
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Directory, Environments.Production);

        await guard.StartAsync(CancellationToken.None);

        var log = Logger.Single(7002);
        log.Level.ShouldBe(LogLevel.Warning);
        log.Properties["Operation"].ShouldBe("startup");
    }

    [Fact]
    public async Task StartAsync_ProviderAvailable_DoesNotLogTheWarning()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Directory, Environments.Production);

        await guard.StartAsync(CancellationToken.None);

        Logger.Contains(7002).ShouldBeFalse();
        Logger.Contains(7005).ShouldBeFalse();
    }

    [Fact]
    public async Task StartAsync_RoundTripReturnsAnotherDocument_RefusesToStartAndLogsTheRejection()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var protector = Substitute.For<IHolderDocumentProtector>();
        protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>())
            .Returns(new ProtectedHolderDocument(new byte[42], new byte[32], 1));
        protector.Unprotect(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<AccountId>())
            .Returns(HolderDocument.From(SecurityVectors.CpfDocument));
        var guard = Guard(provider, protector, PiiProvider.Directory, Environments.Production);

        await Should.ThrowAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));

        var log = Logger.Single(7005);
        log.Level.ShouldBe(LogLevel.Error);
        log.Properties["Reason"].ShouldBe("round_trip_failed");
    }

    [Fact]
    public async Task StartAsync_RoundTripCannotDecrypt_RefusesToStart()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var protector = Substitute.For<IHolderDocumentProtector>();
        protector.Protect(Arg.Any<HolderDocument>(), Arg.Any<AccountId>())
            .Returns(new ProtectedHolderDocument(new byte[42], new byte[32], 1));
        protector.Unprotect(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<AccountId>())
            .Returns(AccountErrors.InvalidHolderDocument);
        var guard = Guard(provider, protector, PiiProvider.Directory, Environments.Production);

        await Should.ThrowAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StopAsync_Completes()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Directory, Environments.Production);

        await Should.NotThrowAsync(() => guard.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartAsync_FailureMessagesNeverCarryKeyMaterial()
    {
        var provider = new FakeKeyProvider(1, SecurityVectors.KeySetOne);
        var guard = Guard(provider, RealProtector(provider), PiiProvider.Configuration, Environments.Production);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));

        failure.ToString().ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
        failure.ToString().ShouldNotContain(SecurityVectors.BlindIndexKeyBase64, Case.Sensitive);
    }
}
