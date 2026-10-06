using Microsoft.Extensions.Logging;

namespace Ledger.Application.Tests.Support;

internal sealed record CapturedLog(
    LogLevel Level,
    EventId EventId,
    string Message,
    IReadOnlyDictionary<string, string?> Properties);
