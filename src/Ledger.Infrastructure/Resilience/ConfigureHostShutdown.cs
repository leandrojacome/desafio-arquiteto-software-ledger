using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Resilience;

internal sealed class ConfigureHostShutdown(IOptions<ResilienceOptions> resilience) : IConfigureOptions<HostOptions>
{
    public void Configure(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ShutdownTimeout = TimeSpan.FromSeconds(resilience.Value.ShutdownTimeoutSeconds);
    }
}
