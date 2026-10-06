using Ledger.Infrastructure.Persistence;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class MigrationOptionsValidatorTests
{
    private readonly MigrationOptionsValidator _validator = new();

    [Fact]
    public void Defaults_AreValidAndWaitTwoMinutesForTheLock()
    {
        var options = new MigrationOptions();

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
        options.LockTimeoutSeconds.ShouldBe(120);
        MigrationOptions.SectionName.ShouldBe("Migrations");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(120)]
    [InlineData(3600)]
    public void LockTimeoutInsideTheRange_IsAccepted(int seconds)
    {
        var result = _validator.Validate(null, new MigrationOptions { LockTimeoutSeconds = seconds });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void LockTimeoutOutsideTheRange_IsRejectedNamingTheKey(int seconds)
    {
        var result = _validator.Validate(null, new MigrationOptions { LockTimeoutSeconds = seconds });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Migrations");
        result.FailureMessage.ShouldContain("LockTimeoutSeconds");
    }
}
