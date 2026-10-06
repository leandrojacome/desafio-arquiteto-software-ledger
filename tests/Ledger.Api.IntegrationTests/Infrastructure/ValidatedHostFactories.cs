extern alias LedgerWorker;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class ValidatedApiFactory(
    IReadOnlyDictionary<string, string?> configuration,
    string environmentName,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            configurationBuilder.AddInMemoryCollection(configuration));

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}

internal sealed class ValidatedWorkerFactory(
    IReadOnlyDictionary<string, string?> configuration,
    string environmentName,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<LedgerWorker::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            configurationBuilder.AddInMemoryCollection(configuration));

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
