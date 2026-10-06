using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Audit;

internal static class AuditServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerAudit(
        this IServiceCollection services,
        IConfiguration configuration,
        PostgresSource auditSource)
    {
        services.AddOptions<DeniedWriteAuditOptions>()
            .Bind(configuration.GetSection(DeniedWriteAuditOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<DeniedWriteAuditOptions>, DeniedWriteAuditOptionsValidator>());

        services.TryAddSingleton<IAuditTrail>(provider => new PostgresAuditTrail(
            provider.GetRequiredService<IPostgresConnectionFactory>(),
            auditSource,
            provider.GetRequiredService<ISecurityTelemetry>()));

        services.TryAddSingleton<IDeniedWriteAuditor, DeniedWriteAuditor>();

        return services;
    }
}
