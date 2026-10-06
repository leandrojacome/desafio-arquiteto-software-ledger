using System.Net;

namespace Ledger.Api.RateLimiting;

internal readonly record struct ClientQuotaKey(bool IsRead, string? ClientId, IPAddress? Origin);
