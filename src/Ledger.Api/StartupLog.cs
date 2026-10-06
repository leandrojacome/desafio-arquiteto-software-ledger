namespace Ledger.Api;

internal static partial class StartupLog
{
    [LoggerMessage(
        EventId = 5040,
        EventName = "ApiConfigurationInvalid",
        Level = LogLevel.Critical,
        Message = "Invalid configuration for the API: {Failures}")]
    public static partial void ApiConfigurationInvalid(ILogger logger, string failures);
}
