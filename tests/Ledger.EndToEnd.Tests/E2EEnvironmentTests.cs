namespace Ledger.EndToEnd.Tests;

[Trait("Category", "E2E")]
public sealed class E2EEnvironmentTests
{
    private const string BaseUrl = "LEDGER_E2E_BASE_URL";
    private const string Skip = "LEDGER_SKIP_E2E";
    private const string Require = "LEDGER_REQUIRE_DOCKER";

    [Fact]
    public void ResolveBaseUrl_WithoutTheVariable_UsesTheComposeAddress()
    {
        var url = E2EEnvironment.ResolveBaseUrl(Variables());

        url.ShouldBe(new Uri("http://localhost:8080"));
    }

    [Theory]
    [InlineData("http://localhost:9090")]
    [InlineData("https://ledger.example.test")]
    public void ResolveBaseUrl_WithAnAbsoluteHttpAddress_UsesIt(string value)
    {
        var url = E2EEnvironment.ResolveBaseUrl(Variables((BaseUrl, value)));

        url.ShouldBe(new Uri(value));
    }

    [Theory]
    [InlineData("localhost:8080")]
    [InlineData("/health")]
    [InlineData("ftp://localhost:8080")]
    [InlineData("not an address")]
    [InlineData("   ")]
    public void ResolveBaseUrl_WithAnythingElse_FailsInsteadOfFallingBack(string value)
    {
        var failure = Should.Throw<InvalidOperationException>(() =>
            E2EEnvironment.ResolveBaseUrl(Variables((BaseUrl, value))));

        failure.Message.ShouldContain(BaseUrl);
        failure.Message.ShouldContain("http or https");
    }

    [Fact]
    public void SkipReason_WithoutTheOptOut_IsNull()
    {
        E2EEnvironment.SkipReason(Variables()).ShouldBeNull();
    }

    [Fact]
    public void SkipReason_WithTheOptOut_NamesTheVariableAndHowToRunTheTests()
    {
        var reason = E2EEnvironment.SkipReason(Variables((Skip, "true")));

        reason.ShouldNotBeNull();
        reason.ShouldContain(Skip);
        reason.ShouldContain("LEDGER_E2E_PROVISION=true dotnet test tests/Ledger.EndToEnd.Tests");
        reason.ShouldContain("docker compose up -d --build --wait");
    }

    [Fact]
    public void SkipReason_WithTheRequireVariable_IsNeverSet()
    {
        E2EEnvironment.SkipReason(Variables((Require, "true"), (Skip, "true"))).ShouldBeNull();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("1")]
    public void SkipReason_WhenTheOptOutIsNotTrue_IsNull(string value)
    {
        E2EEnvironment.SkipReason(Variables((Skip, value))).ShouldBeNull();
    }

    [Fact]
    public void ShouldProvision_OnlyWhenTheVariableIsTrue()
    {
        E2EEnvironment.ShouldProvision(Variables()).ShouldBeFalse();
        E2EEnvironment.ShouldProvision(Variables(("LEDGER_E2E_PROVISION", "false"))).ShouldBeFalse();
        E2EEnvironment.ShouldProvision(Variables(("LEDGER_E2E_PROVISION", "1"))).ShouldBeFalse();
        E2EEnvironment.ShouldProvision(Variables(("LEDGER_E2E_PROVISION", "true"))).ShouldBeTrue();
    }

    [Fact]
    public void ResolveWorkerUrl_AndResolveManagementUrl_HaveTheComposeDefaults()
    {
        E2EEnvironment.ResolveWorkerUrl(Variables()).ShouldBe(new Uri("http://localhost:8081"));
        E2EEnvironment.ResolveManagementUrl(Variables()).ShouldBe(new Uri("http://localhost:15672"));
    }

    [Fact]
    public void ResolveWorkerUrl_WithAnInvalidAddress_FailsNamingTheVariable()
    {
        var failure = Should.Throw<InvalidOperationException>(() =>
            E2EEnvironment.ResolveWorkerUrl(Variables(("LEDGER_E2E_WORKER_URL", "worker:8081"))));

        failure.Message.ShouldContain("LEDGER_E2E_WORKER_URL");
        failure.Message.ShouldContain("http or https");
    }
    private static Func<string, string?> Variables(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

        return name => map.GetValueOrDefault(name);
    }
}
