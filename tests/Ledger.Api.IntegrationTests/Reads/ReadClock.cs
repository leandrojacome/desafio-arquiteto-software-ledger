namespace Ledger.Api.IntegrationTests.Reads;

internal static class ReadClock
{
    public static DateTimeOffset UtcNow => TimeProvider.System.GetUtcNow();
}
