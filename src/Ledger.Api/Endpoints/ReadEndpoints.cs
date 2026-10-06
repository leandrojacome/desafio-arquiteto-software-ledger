namespace Ledger.Api.Endpoints;

internal static class ReadEndpoints
{
    public static void MapReadEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapBalanceEndpoints();
        group.MapStatementEndpoints();
    }
}
