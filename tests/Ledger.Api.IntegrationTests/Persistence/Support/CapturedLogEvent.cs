using Microsoft.Extensions.Logging;

namespace Ledger.Api.IntegrationTests.Persistence.Support;

internal sealed record CapturedLogEvent(
    int EventId,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);
