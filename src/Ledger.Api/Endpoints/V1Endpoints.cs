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

internal static class ReadEndpoints
{
    public static void MapReadEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapBalanceEndpoints();
        group.MapStatementEndpoints();
    }
}

internal static class WriteEndpoints
{
    public static void MapWriteEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapAccountEndpoints();
        group.MapEntryEndpoints();
    }
}
