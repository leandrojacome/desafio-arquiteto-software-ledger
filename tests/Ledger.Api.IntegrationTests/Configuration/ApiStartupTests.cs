using System.Diagnostics;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;

namespace Ledger.Api.IntegrationTests.Configuration;

[Trait("Category", "Integration")]
public sealed class ApiStartupTests
{
    private const int InvalidConfigurationExitCode = 3;

    public static TheoryData<string, string> InvalidSettings => new()
    {
        { "RateLimiting:WritePerClient:Capacity", "0" },
        { "RateLimiting:WriteConcurrency", "0" },
        { "Ledger:Statement:MaxLimit", "0" },
        { "Resilience:RequestTimeoutSeconds", "0" },
        { "Security:Cursor:SigningKey", "AAAA" },
        { "Authentication:Audience", string.Empty },
        { "Postgres:Port", "70000" }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public async Task AProcessWithAnInvalidSetting_ExitsWithCode3WithoutListening(string key, string value)
    {
        var settings = BaseSettings();

        settings[key] = value;

        var run = await RunAsync("Testing", settings);

        run.ExitCode.ShouldBe(InvalidConfigurationExitCode);
        run.Output.ShouldContain("Invalid configuration for the API");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Homolog")]
    public async Task AnApiInAStrictEnvironmentWithTheDevelopmentSettings_ExitsWithCode3NamingTheBrokenRules(
        string environment)
    {
        var run = await RunAsync(environment, BaseSettings());

        run.ExitCode.ShouldBe(InvalidConfigurationExitCode);
        run.Output.ShouldContain("Postgres:SslMode", Case.Sensitive, run.Output);
        run.Output.ShouldContain("VerifyFull", Case.Sensitive, run.Output);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task AnApiInAStrictEnvironmentWithALocalKey_ExitsWithCode3NamingTheAuthenticationRule(string environment)
    {
        using var keys = TemporaryKeyDirectory.WithVersionOne();
        var settings = ProductionLikeSettings.Create(keys);

        settings["Authentication:Mode"] = "LocalKey";
        settings["Authentication:LocalKey:SigningKey"] = TestConfiguration.SigningKey;

        var run = await RunAsync(environment, settings);

        run.ExitCode.ShouldBe(InvalidConfigurationExitCode);
        run.Output.ShouldContain("Authentication:Mode", Case.Sensitive, run.Output);
        run.Output.ShouldNotContain(TestConfiguration.SigningKey);
    }

    [Fact]
    public async Task TheReportOfAnInvalidConfiguration_NeverPrintsASecret()
    {
        var settings = BaseSettings();

        settings["RateLimiting:WriteConcurrency"] = "0";
        settings["Security:Cursor:SigningKey"] = "AAAA";

        var run = await RunAsync("Production", settings);

        run.ExitCode.ShouldBe(InvalidConfigurationExitCode);
        run.Output.ShouldNotContain(TestConfiguration.SigningKey);
        run.Output.ShouldNotContain(TestConfiguration.PiiEncryptionKeyOne);
        run.Output.ShouldNotContain(TestConfiguration.PiiBlindIndexKeyOne);
        run.Output.ShouldNotContain(TestConfiguration.CursorSigningKey);
        run.Output.ShouldNotContain("integration-tests-password");
        run.Output.ShouldNotContain("integration-tests-broker-password");
    }

    [Fact]
    public async Task TheReport_IsASingleCriticalLogLine()
    {
        var settings = BaseSettings();

        settings["RateLimiting:WriteConcurrency"] = "0";

        var run = await RunAsync("Testing", settings);

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Contains("Invalid configuration for the API", StringComparison.Ordinal))
            .ToList();

        lines.ShouldHaveSingleItem();
        lines[0].ShouldContain("\"@l\":\"Fatal\"");
        run.Output.ShouldNotContain("Hosting failed to start");
    }

    private static Dictionary<string, string?> BaseSettings() => TestConfiguration.ForUnreachablePostgres();

    private static async Task<StartupRun> RunAsync(string environment, Dictionary<string, string?> settings)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(RepositoryPaths.Source, "Ledger.Api"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Ledger.Api.dll"));
        start.ArgumentList.Add($"--environment={environment}");

        foreach (var (key, value) in settings.Where(pair => pair.Value is not null))
        {
            start.ArgumentList.Add($"--{key}={value}");
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The API process did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            throw;
        }

        return new StartupRun(process.ExitCode, await output + await errors);
    }

    private sealed record StartupRun(int ExitCode, string Output);
}
