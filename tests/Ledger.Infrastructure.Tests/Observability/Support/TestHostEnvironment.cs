using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Ledger.Infrastructure.Tests.Observability.Support;

internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "ledger-tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
