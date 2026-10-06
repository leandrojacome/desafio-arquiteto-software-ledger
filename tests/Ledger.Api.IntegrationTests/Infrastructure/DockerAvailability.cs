using System.ComponentModel;
using System.Diagnostics;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class DockerAvailability
{
    private const string DisableVariable = "LEDGER_TESTS_DISABLE_DOCKER";

    private const string RequireVariable = "LEDGER_REQUIRE_DOCKER";

    private const string EnabledValue = "true";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    private static readonly Lazy<bool> Probe = new(Detect);

    public static bool IsAvailable => Probe.Value;

    public static string MissingReason =>
        $"Docker is not responding, and these tests need it to start a PostgreSQL container. Start Docker Desktop (or the Docker daemon) and run the tests again. To skip the tests that need Docker on purpose, set {DisableVariable}={EnabledValue}.";

    public static string? SkipReason() => SkipReason(Environment.GetEnvironmentVariable);

    public static string? SkipReason(Func<string, string?> variable)
    {
        if (IsOn(variable, RequireVariable) || !IsOn(variable, DisableVariable))
        {
            return null;
        }

        return $"{DisableVariable}={EnabledValue}: the tests that need a PostgreSQL container were skipped on purpose. Unset the variable and start Docker to run them.";
    }

    private static bool IsOn(Func<string, string?> variable, string name) =>
        string.Equals(variable(name), EnabledValue, StringComparison.OrdinalIgnoreCase);

    private static bool Detect()
    {
        try
        {
            var startInfo = new ProcessStartInfo("docker", "version --format {{.Server.Version}}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(ProbeTimeout))
            {
                process.Kill(entireProcessTree: true);

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
