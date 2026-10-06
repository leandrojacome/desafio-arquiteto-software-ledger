namespace Ledger.Api.IntegrationTests.Infrastructure;

[Trait("Category", "Integration")]
public sealed class DockerFactAttributeTests
{
    private const string Disable = "LEDGER_TESTS_DISABLE_DOCKER";
    private const string Require = "LEDGER_REQUIRE_DOCKER";

    [Fact]
    public void DockerFact_WithoutAnyVariable_IsNotSkipped()
    {
        var attribute = new DockerFactAttribute(Variables());

        attribute.Skip.ShouldBeNull();
    }

    [Fact]
    public void DockerFact_WhenDockerIsDisabledOnPurpose_IsSkippedWithTheReason()
    {
        var attribute = new DockerFactAttribute(Variables((Disable, "true")));

        attribute.Skip.ShouldNotBeNull();
        attribute.Skip.ShouldContain(Disable);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    public void DockerFact_WhenTheOptOutIsNotTrue_IsNotSkipped(string value)
    {
        var attribute = new DockerFactAttribute(Variables((Disable, value)));

        attribute.Skip.ShouldBeNull();
    }

    [Fact]
    public void DockerFact_WithTheRequireVariable_IsNeverSkipped()
    {
        var attribute = new DockerFactAttribute(Variables((Require, "true"), (Disable, "true")));

        attribute.Skip.ShouldBeNull();
    }

    [Fact]
    public void DockerTheory_WhenDockerIsDisabledOnPurpose_IsSkippedWithTheReason()
    {
        var attribute = new DockerTheoryAttribute(Variables((Disable, "true")));

        attribute.Skip.ShouldNotBeNull();
        attribute.Skip.ShouldContain(Disable);
    }

    [Fact]
    public void DockerTheory_WithoutAnyVariable_IsNotSkipped()
    {
        var attribute = new DockerTheoryAttribute(Variables());

        attribute.Skip.ShouldBeNull();
    }

    [Fact]
    public void MissingReason_TellsHowToStartDockerAndHowToSkipOnPurpose()
    {
        DockerAvailability.MissingReason.ShouldContain("Start Docker");
        DockerAvailability.MissingReason.ShouldContain($"{Disable}=true");
    }

    private static Func<string, string?> Variables(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

        return name => map.GetValueOrDefault(name);
    }
}
