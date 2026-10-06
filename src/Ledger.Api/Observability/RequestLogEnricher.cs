using Ledger.Api.Security;
using Serilog;

namespace Ledger.Api.Observability;

internal static class RequestLogEnricher
{
    public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        var endpoint = context.GetEndpoint() as RouteEndpoint;

        diagnosticContext.Set("RequestRoute", endpoint?.RoutePattern.RawText ?? context.Request.Path.Value);
        diagnosticContext.Set("RequestMethod", context.Request.Method);

        var clientId = context.User.FindFirst(ClaimNames.ClientId)?.Value;

        if (clientId is not null)
        {
            diagnosticContext.Set("ClientId", clientId);
        }
    }
}
