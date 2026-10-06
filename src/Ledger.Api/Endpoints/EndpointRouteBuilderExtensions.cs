using Ledger.Api.OpenApi;

namespace Ledger.Api.Endpoints;

internal static class EndpointRouteBuilderExtensions
{
    public static void MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthEndpoints();
        app.MapV1Group();
        app.MapLedgerOpenApi();
    }
}
