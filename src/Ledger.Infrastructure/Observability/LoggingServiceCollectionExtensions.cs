using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Ledger.Infrastructure.Observability;

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerLogging(this IServiceCollection services, string serviceName)
    {
        services.AddSerilog((serviceProvider, logger) => logger
            .ReadFrom.Configuration(serviceProvider.GetRequiredService<IConfiguration>())
            .ReadFrom.Services(serviceProvider)
            .WithLedgerIdentity(serviceName, serviceProvider.GetRequiredService<IHostEnvironment>().EnvironmentName)
            .WithLedgerEnrichers()
            .WithLedgerProtection()
            .WithoutConfigurationFailureTraces()
            .WithoutDuplicatedHealthCheckVerdicts()
            .WriteLedgerJsonToConsole());

        return services;
    }
}
