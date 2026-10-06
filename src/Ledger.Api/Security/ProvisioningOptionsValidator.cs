using Ledger.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class ProvisioningOptionsValidator(IHostEnvironment environment) : IValidateOptions<ProvisioningOptions>
{
    private const string Key = $"{ProvisioningOptions.SectionName}:AccountProvisioningClients";

    public ValidateOptionsResult Validate(string? name, ProvisioningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var clients = options.AccountProvisioningClients;

        if (clients.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add($"{Key}: entries must not be blank.");
        }

        if (environment.RequiresProductionControls())
        {
            if (clients.Length == 0)
            {
                failures.Add($"{Key}: must list the clients allowed to create accounts outside Development and Testing.");
            }

            if (clients.Contains(ProvisioningOptions.Wildcard, StringComparer.Ordinal))
            {
                failures.Add($"{Key}: the wildcard is only accepted in Development and Testing.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
