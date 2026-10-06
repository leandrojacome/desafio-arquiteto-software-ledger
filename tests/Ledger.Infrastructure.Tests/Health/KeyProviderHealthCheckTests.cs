using Ledger.Application.Abstractions;
using Ledger.Application.Tests.Security;
using Ledger.Infrastructure.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Ledger.Infrastructure.Tests.Health;

[Trait("Category", "Unit")]
public sealed class KeyProviderHealthCheckTests
{
    private static async Task<HealthCheckResult> CheckAsync(IKeyProvider provider)
    {
        return await new KeyProviderHealthCheck(provider).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task CheckHealthAsync_ProviderDeliversTheActiveSet_IsHealthy()
    {
        var result = await CheckAsync(new FakeKeyProvider(1, SecurityVectors.KeySetOne));

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ProviderUnavailable_IsDegradedAndNeverUnhealthy()
    {
        var result = await CheckAsync(new FakeKeyProvider(1, SecurityVectors.KeySetOne).WentDown());

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckHealthAsync_VersionVanishedFromTheSource_IsDegradedAndNamesTheVersions()
    {
        var provider = new FakeKeyProvider(2, SecurityVectors.KeySetTwo).WithVanished(1);

        var result = await CheckAsync(provider);

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Description.ShouldNotBeNull().ShouldContain("1");
        result.Description.ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
    }

    [Fact]
    public async Task CheckHealthAsync_ReadsOnlyTheAvailabilityFlagAndTheVanishedVersions()
    {
        var provider = Substitute.For<IKeyProvider>();
        provider.IsAvailable.Returns(true);

        await CheckAsync(provider);

        _ = provider.Received(1).IsAvailable;
        _ = provider.Received(1).VanishedVersions;
        _ = provider.DidNotReceive().Active;
        _ = provider.DidNotReceive().Live;
    }

    [Fact]
    public async Task CheckHealthAsync_Description_NeverCarriesKeyMaterial()
    {
        var result = await CheckAsync(new FakeKeyProvider(1, SecurityVectors.KeySetOne).WentDown());

        result.Description.ShouldNotBeNull().ShouldNotContain(SecurityVectors.EncryptionKeyBase64, Case.Sensitive);
        result.Exception.ShouldBeNull();
    }
}
