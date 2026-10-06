using System.Net;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    private const string Key = $"{ForwardedHeadersSettings.SectionName}:{nameof(ForwardedHeadersSettings.KnownNetworks)}";

    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        for (var index = 0; index < options.KnownNetworks.Length; index++)
        {
            if (!IPNetwork.TryParse(options.KnownNetworks[index], out var network))
            {
                failures.Add($"{Key}:{index} must be a network in CIDR notation, such as 10.0.0.0/8.");
            }
            else if (network.PrefixLength == 0)
            {
                failures.Add($"{Key}:{index} must not trust every address.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
