using Ledger.Infrastructure.Hosting;
using Microsoft.OpenApi;

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

internal static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddLedgerOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(OpenApiNames.DocumentName, options =>
        {
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer<LedgerDocumentTransformer>();
            options.AddOperationTransformer<LedgerOperationTransformer>();
            options.AddSchemaTransformer<LedgerSchemaTransformer>();
        });

        return services;
    }
}
