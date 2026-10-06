using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Observability;

public static class StartupLogger
{
    private const string AspNetCoreEnvironmentVariable = "ASPNETCORE_ENVIRONMENT";
    private const string DotNetEnvironmentVariable = "DOTNET_ENVIRONMENT";

    public static Logger Create(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WithLedgerIdentity(serviceName, ResolveEnvironmentName())
            .WithLedgerEnrichers()
            .WithLedgerProtection()
            .WriteLedgerJsonToConsole()
            .CreateLogger();
    }

    private static string ResolveEnvironmentName()
    {
        return Environment.GetEnvironmentVariable(AspNetCoreEnvironmentVariable)
               ?? Environment.GetEnvironmentVariable(DotNetEnvironmentVariable)
               ?? Environments.Production;
    }
}
