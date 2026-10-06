namespace Ledger.Api.Endpoints;

internal static class WriteEndpoints
{
    public static void MapWriteEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapAccountEndpoints();
        group.MapEntryEndpoints();
    }
}
