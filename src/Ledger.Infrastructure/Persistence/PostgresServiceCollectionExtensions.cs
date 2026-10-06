using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Persistence;

internal static class PostgresServiceCollectionExtensions
{
    private const string PostgresHealthCheckName = "postgres";
    private const string SchemaHealthCheckName = "schema";

    public static IServiceCollection AddLedgerPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyCollection<PostgresSource> sources)
    {
        var selection = new PostgresSourceSelection(sources);

        NpgsqlRuntimeSwitches.Apply();

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<ResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<MigrationOptions>()
            .Bind(configuration.GetSection(MigrationOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<PostgresOptions>>(provider =>
            new PostgresOptionsValidator(selection.Sources, provider.GetRequiredService<IHostEnvironment>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MigrationOptions>, MigrationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HostOptions>, ConfigureHostShutdown>());

        services.AddSingleton(selection);
        services.AddSingleton<PostgresConnectionFactory>();
        services.AddSingleton<IPostgresConnectionFactory>(provider =>
            provider.GetRequiredService<PostgresConnectionFactory>());
        services.AddSingleton<SchemaVersionReader>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IDatabaseMigrator>(provider => provider.GetRequiredService<MigrationRunner>());
        services.AddLedgerFailureClassification();
        services.AddSingleton<IDependencyFailureInspector, PostgresDependencyFailureInspector>();
        services.AddHostedService<PostgresPoolWarmupService>();

        services.AddSingleton<PostgresReadinessChecks>();
        services.AddHealthChecks()
            .Add(Readiness(PostgresHealthCheckName, provider => provider.GetRequiredService<PostgresReadinessChecks>().Postgres))
            .Add(Readiness(SchemaHealthCheckName, provider => provider.GetRequiredService<PostgresReadinessChecks>().Schema));

        return services;
    }

    private static HealthCheckRegistration Readiness(string name, Func<IServiceProvider, IHealthCheck> create) =>
        new(name, create, failureStatus: null, tags: [HealthCheckTags.Ready]);
}
