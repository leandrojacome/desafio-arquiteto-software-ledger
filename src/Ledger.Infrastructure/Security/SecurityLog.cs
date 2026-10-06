using Microsoft.Extensions.Logging;

namespace Ledger.Infrastructure.Security;

internal static partial class SecurityLog
{
    [LoggerMessage(
        EventId = 7003,
        EventName = "KeyReloadFailed",
        Level = LogLevel.Warning,
        Message = "The key set could not be reloaded ({ExceptionType}); the previous one stays in use")]
    public static partial void KeyReloadFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 7004,
        EventName = "KeySetReloaded",
        Level = LogLevel.Information,
        Message = "Key sets reloaded: versions {Versions}, active version {ActiveVersion}")]
    public static partial void KeySetReloaded(ILogger logger, string versions, int activeVersion);

    [LoggerMessage(
        EventId = 7005,
        EventName = "KeyMaterialRejected",
        Level = LogLevel.Error,
        Message = "The key material was rejected ({Reason}); {Consequence}")]
    public static partial void KeyMaterialRejected(ILogger logger, string reason, string consequence);

    [LoggerMessage(
        EventId = 7009,
        EventName = "KeyVersionsVanished",
        Level = LogLevel.Warning,
        Message = "Key versions {Versions} vanished from the key source; accounts still sealed with them can no longer be read")]
    public static partial void KeyVersionsVanished(ILogger logger, string versions);
}
