namespace Ledger.Infrastructure.Persistence;

internal static class NpgsqlRuntimeSwitches
{
    public const string DisableDateTimeInfinityConversions = "Npgsql.DisableDateTimeInfinityConversions";

    public static void Apply() => AppContext.SetSwitch(DisableDateTimeInfinityConversions, true);
}
