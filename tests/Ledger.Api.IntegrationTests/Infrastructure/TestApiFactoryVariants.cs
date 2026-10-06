using Microsoft.AspNetCore.Routing;

namespace Ledger.Api.IntegrationTests.Infrastructure;

public sealed class DefaultTestApiFactory : TestApiFactory
{
    public DefaultTestApiFactory()
        : base()
    {
    }
}

public sealed class KestrelApiFactory : TestApiFactory
{
    public KestrelApiFactory()
        : base()
    {
        UseKestrel();
    }

    internal KestrelApiFactory(
        IReadOnlyDictionary<string, string?> overrides,
        Action<IEndpointRouteBuilder>? routes = null)
        : base(overrides, routes: routes)
    {
        UseKestrel();
    }

    public HttpClient CreateKestrelClient()
    {
        StartServer();

        return CreateClient();
    }
}
