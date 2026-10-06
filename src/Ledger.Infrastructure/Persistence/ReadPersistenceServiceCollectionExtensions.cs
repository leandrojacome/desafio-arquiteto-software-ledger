using Ledger.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Infrastructure.Persistence;

internal static class ReadPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerReaders(this IServiceCollection services)
    {
        services.AddSingleton<IBalanceReader, PostgresBalanceReader>();
        services.AddSingleton<IStatementReader, PostgresStatementReader>();

        return services;
    }
}
