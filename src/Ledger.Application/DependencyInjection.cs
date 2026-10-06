using Ledger.Application.Accounts;
using Ledger.Application.Balances;
using Ledger.Application.Entries;
using Ledger.Application.Idempotency;
using Ledger.Application.Integrity;
using Ledger.Application.Outbox;
using Ledger.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ReadAudit>();

        services.AddScoped<CreateAccountHandler>();
        services.AddScoped<RegisterEntryHandler>();
        services.AddScoped<ReverseEntryHandler>();
        services.AddScoped<GetBalanceHandler>();
        services.AddScoped<ListEntriesHandler>();
        services.AddScoped<PublishOutboxBatchHandler>();
        services.AddScoped<PruneOutboxHandler>();
        services.AddScoped<PruneIdempotencyKeysHandler>();
        services.AddScoped<MeasureOutboxHandler>();
        services.AddScoped<RunIntegrityCheckHandler>();
        services.AddScoped<InspectAccountIntegrityHandler>();
        services.AddScoped<RewrapAccountsHandler>();
        services.AddScoped<RecordKeyActivationHandler>();

        return services;
    }
}
