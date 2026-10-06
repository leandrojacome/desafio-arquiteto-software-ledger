extern alias LedgerWorker;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal class WorkerFactory : WebApplicationFactory<LedgerWorker::Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configuration;

    public WorkerFactory()
        : this(TestConfiguration.ForUnreachablePostgres())
    {
    }

    protected WorkerFactory(IReadOnlyDictionary<string, string?> configuration)
    {
        _configuration = configuration;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_configuration));
    }
}
