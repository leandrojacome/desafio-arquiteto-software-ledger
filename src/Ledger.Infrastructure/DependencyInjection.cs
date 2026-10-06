using Ledger.Infrastructure.Messaging;
using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        params PostgresSource[] sources)
    {
        if (sources.Length == 0)
        {
            throw new ArgumentException("At least one PostgreSQL source must be informed.", nameof(sources));
        }

        services.AddLedgerPersistence(configuration, sources);
        services.AddLedgerObservability();
        services.AddLedgerSecurity(configuration, sources);
        services.AddLedgerMessaging(configuration, sources.Contains(PostgresSource.Worker));

        return services;
    }
}
