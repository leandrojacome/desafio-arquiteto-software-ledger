using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Health;

internal static class HealthServiceCollectionExtensions
{
    private const string ShutdownName = "shutdown";

    public static IServiceCollection AddLedgerApiHealth(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
            new HealthRetryAfter(provider.GetRequiredService<IOptions<ResilienceOptions>>().Value.Health.RetryAfterSeconds));

        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                ShutdownName,
                provider => new ShutdownHealthCheck(provider.GetRequiredService<IHostApplicationLifetime>()),
                failureStatus: null,
                tags: [HealthCheckTags.Ready]));

        return services;
    }
}
