using System.ComponentModel;
using System.Diagnostics;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal static class DockerCli
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    public static async Task RunAsync(string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;

        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("The docker CLI could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("The docker CLI is not available on this machine.", exception);
        }

        using (process)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(CommandTimeout);

            var error = process.StandardError.ReadToEndAsync(deadline.Token);

            await process.WaitForExitAsync(deadline.Token);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"docker {arguments} failed with exit code {process.ExitCode}: {await error}");
            }
        }
    }
}
