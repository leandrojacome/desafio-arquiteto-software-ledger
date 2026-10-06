using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Ledger.Api.IntegrationTests.Infrastructure;

public class LedgerApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configuration;

    public LedgerApiFactory()
        : this(TestConfiguration.ForUnreachablePostgres())
    {
    }

    protected LedgerApiFactory(IReadOnlyDictionary<string, string?> configuration)
    {
        _configuration = configuration;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_configuration));
    }
}
