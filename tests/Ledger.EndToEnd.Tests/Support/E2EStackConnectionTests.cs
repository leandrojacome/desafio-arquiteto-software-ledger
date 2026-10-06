namespace Ledger.EndToEnd.Tests.Support;

[Trait("Category", "E2E")]
public sealed class E2EStackConnectionTests
{
    private const string PortVariable = "POSTGRES_PORT";

    [Fact]
    public void AdminConnectionFromFile_WithThePortInTheEnvironment_PrefersItOverTheEnvFile()
    {
        var file = EnvFile(("POSTGRES_SUPERUSER_PASSWORD", "dev-only"), (PortVariable, "5432"));

        var connection = E2EStack.AdminConnectionFromFile(Variables((PortVariable, "55432")), file);

        connection.ShouldNotBeNull();
        connection.ShouldContain("Port=55432;");
        connection.ShouldContain("Username=postgres;");
        connection.ShouldContain("Password=dev-only;");
    }

    [Fact]
    public void AdminConnectionFromFile_WithoutThePortInTheEnvironment_UsesTheEnvFile()
    {
        var file = EnvFile(("POSTGRES_SUPERUSER_PASSWORD", "dev-only"), (PortVariable, "55433"));

        var connection = E2EStack.AdminConnectionFromFile(Variables(), file);

        connection.ShouldNotBeNull();
        connection.ShouldContain("Port=55433;");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AdminConnectionFromFile_WithoutAnyPort_FallsBackToThePostgresDefault(string? environmentValue)
    {
        var file = EnvFile(("POSTGRES_SUPERUSER_PASSWORD", "dev-only"));
        var variables = environmentValue is null ? Variables() : Variables((PortVariable, environmentValue));

        var connection = E2EStack.AdminConnectionFromFile(variables, file);

        connection.ShouldNotBeNull();
        connection.ShouldContain("Port=5432;");
    }

    [Fact]
    public void AdminConnectionFromFile_WithoutTheSuperuserPassword_ReturnsNull()
    {
        var file = EnvFile((PortVariable, "55432"));

        E2EStack.AdminConnectionFromFile(Variables((PortVariable, "55432")), file).ShouldBeNull();
    }

    [Fact]
    public void ConnectionFromFile_WithThePortInTheEnvironment_PrefersItOverTheEnvFile()
    {
        var file = EnvFile(("LEDGER_WORKER_PASSWORD", "dev-only"), (PortVariable, "5432"));

        var connection = E2EStack.ConnectionFromFile(Variables((PortVariable, "55432")), file);

        connection.ShouldNotBeNull();
        connection.ShouldContain("Port=55432;");
        connection.ShouldContain("Username=ledger_worker;");
    }

    [Fact]
    public void ConnectionFromFile_WithoutTheWorkerPassword_ReturnsNull()
    {
        E2EStack.ConnectionFromFile(Variables((PortVariable, "55432")), EnvFile()).ShouldBeNull();
    }

    private static Dictionary<string, string> EnvFile(params (string Name, string Value)[] values) =>
        values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

    private static Func<string, string?> Variables(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);

        return name => map.GetValueOrDefault(name);
    }
}
