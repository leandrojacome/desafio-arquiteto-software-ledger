using Ledger.Infrastructure.Hosting;

namespace Ledger.Api.OpenApi;

internal static class OpenApiEndpoints
{
    public static void MapLedgerOpenApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var environment = app.ServiceProvider.GetRequiredService<IHostEnvironment>();

        if (environment.RequiresProductionControls())
        {
            return;
        }

        app.MapOpenApi(ApiConstants.OpenApiRoute).AllowAnonymous();
    }
}
