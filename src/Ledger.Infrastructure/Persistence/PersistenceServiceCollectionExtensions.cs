using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Persistence.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure.Persistence;

internal static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyCollection<PostgresSource> sources)
    {
        return services
            .AddLedgerPostgres(configuration, sources)
            .AddLedgerWriteStore()
            .AddLedgerReaders()
            .AddLedgerBalanceSettings(configuration);
    }

    public static IServiceCollection AddLedgerWriteStore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator, Uuid7IdGenerator>();
        services.AddSingleton<WriteRetryPipeline>();
        services.AddScoped<IUnitOfWork, PostgresUnitOfWork>();

        return services;
    }
}

internal static class ReadPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerReaders(this IServiceCollection services)
    {
        services.AddSingleton<IBalanceReader, PostgresBalanceReader>();
        services.AddSingleton<IStatementReader, PostgresStatementReader>();

        return services;
    }
}
