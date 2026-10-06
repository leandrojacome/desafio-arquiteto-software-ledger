using Microsoft.Extensions.Hosting;

namespace Ledger.Infrastructure.Hosting;

public static class HostEnvironmentExtensions
{
    private static readonly string[] RelaxedEnvironments = [Environments.Development, "Testing"];

    public static bool RequiresProductionControls(this IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return !RelaxedEnvironments.Contains(environment.EnvironmentName, StringComparer.OrdinalIgnoreCase);
    }
}
