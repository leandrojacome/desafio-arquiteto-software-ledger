using Ledger.Application.Abstractions;
using Ledger.Infrastructure.Health;
using Ledger.Infrastructure.Persistence.Integrity;
using Ledger.Infrastructure.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ledger.Infrastructure.Messaging;

internal static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        AddOptions<RabbitMqOptions>(services, configuration, RabbitMqOptions.SectionName, validateOnStart);
        AddOptions<OutboxOptions>(services, configuration, OutboxOptions.SectionName, validateOnStart);
        AddOptions<BrokerCircuitOptions>(services, configuration, BrokerCircuitOptions.SectionName, validateOnStart);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OutboxOptions>, OutboxOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<BrokerCircuitOptions>, BrokerCircuitOptionsValidator>());

        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<OutboxOptions>>().Value.ToSettings());
        services.TryAddSingleton<IWorkerHeartbeat, WorkerHeartbeat>();

        services.TryAddSingleton<BrokerConnection>();
        services.TryAddSingleton<RabbitMqEventPublisher>();
        services.TryAddSingleton<IEventPublisher>(provider => new CircuitBreakingEventPublisher(
            provider.GetRequiredService<RabbitMqEventPublisher>(),
            provider.GetRequiredService<IOptions<BrokerCircuitOptions>>(),
            provider.GetRequiredService<IOutboxTelemetry>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<CircuitBreakingEventPublisher>>()));

        services.AddLedgerOutboxQueue(configuration, validateOnStart);
        services.AddLedgerIntegrity();

        return services;
    }

    private static void AddOptions<TOptions>(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        bool validateOnStart)
        where TOptions : class
    {
        var builder = services.AddOptions<TOptions>().Bind(configuration.GetSection(sectionName));

        if (validateOnStart)
        {
            builder.ValidateOnStart();
        }
    }
}
