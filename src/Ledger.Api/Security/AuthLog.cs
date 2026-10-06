namespace Ledger.Api.Security;

internal static partial class AuthLog
{
    [LoggerMessage(
        EventId = 6001,
        EventName = "AuthenticationRejected",
        Level = LogLevel.Information,
        Message = "Authentication rejected ({Reason}) on {RequestRoute}")]
    public static partial void AuthenticationRejected(ILogger logger, string reason, string requestRoute);

    [LoggerMessage(
        EventId = 6002,
        EventName = "AuthorizationDenied",
        Level = LogLevel.Information,
        Message = "Authorization denied ({Reason}) for client {ClientId} on {RequestRoute}, required scope {RequiredScope}")]
    public static partial void AuthorizationDenied(
        ILogger logger,
        string reason,
        string? clientId,
        string requestRoute,
        string? requiredScope);
}
