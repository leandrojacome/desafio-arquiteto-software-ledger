using System.Diagnostics;
using Ledger.Api.Security;
using Ledger.Application;

namespace Ledger.Api.Middleware;

internal sealed class ClientIdActivityMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var clientId = context.User.FindFirst(ClaimNames.ClientId)?.Value;

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            Activity.Current?.SetTag(ActivityTagNames.ClientId, clientId);
        }

        return next(context);
    }
}
