namespace Ledger.Api.Endpoints;

internal static class V1Endpoints
{
    public static void MapV1Group(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup(ApiConstants.V1RoutePrefix)
            .RequireAuthorization();

        group.MapWriteEndpoints();
        group.MapReadEndpoints();
    }
}
