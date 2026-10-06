using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Audit;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure.Security;

internal static class LedgerSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerSecurity(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyCollection<PostgresSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var isApi = sources.Contains(PostgresSource.Write);
        var auditSource = isApi || !sources.Contains(PostgresSource.Worker)
            ? PostgresSource.Write
            : PostgresSource.Worker;

        services.TryAddSingleton(TimeProvider.System);

        services.AddLedgerPii(configuration, registerHealthCheck: isApi);
        services.AddLedgerCursor(configuration, validateOnStart: sources.Contains(PostgresSource.Statement));
        services.AddLedgerAudit(configuration, auditSource);

        services.AddSingleton<IAccountKeyRewrapper, PostgresAccountKeyRewrapper>();
        services.AddLedgerFailureClassification();

        return services;
    }
}
