extern alias LedgerWorker;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class MessagingWorkerFactory(
    IReadOnlyDictionary<string, string?> configuration,
    Action<IServiceCollection>? configureServices = null,
    string environmentName = "Testing") : WebApplicationFactory<LedgerWorker::Program>
{
    public MessagingWorkerFactory Started()
    {
        _ = Server;

        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            configurationBuilder.AddInMemoryCollection(configuration));

        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
