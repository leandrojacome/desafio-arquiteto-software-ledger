using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class ConfigureForwardedHeadersOptions(IOptions<ForwardedHeadersSettings> settings)
    : IConfigureOptions<ForwardedHeadersOptions>
{
    public void Configure(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var network in settings.Value.KnownNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
    }
}
