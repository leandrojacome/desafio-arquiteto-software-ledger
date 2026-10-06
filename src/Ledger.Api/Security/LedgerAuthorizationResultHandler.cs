using Ledger.Api.Middleware;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Ledger.Api.Security;

internal sealed class LedgerAuthorizationResultHandler(
    IDeniedWriteAuditor auditor,
    ILogger<LedgerAuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private const string AccountIdRouteValue = "accountId";

    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden)
        {
            Deny(context, authorizeResult);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private static string ChallengeFor(string? scope)
    {
        return scope is null
            ? "Bearer error=\"insufficient_scope\""
            : $"Bearer error=\"insufficient_scope\", scope=\"{scope}\"";
    }

    private static string ReasonFor(HttpContext context, bool hasClient)
    {
        if (hasClient)
        {
            return AuthFailureReasons.InsufficientScope;
        }

        return context.Items.ContainsKey(AuthenticationEvents.InvalidClientIdItem)
            ? AuthFailureReasons.InvalidClientId
            : AuthFailureReasons.MissingClientId;
    }

    private static bool IsWrite(HttpContext context) =>
        WriteMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase);

    private static AccountId? AccountOf(HttpContext context)
    {
        var raw = context.Request.RouteValues[AccountIdRouteValue] as string;
        var parsed = AccountId.From(raw);

        return parsed.IsSuccess ? parsed.Value : null;
    }

    private void Deny(HttpContext context, PolicyAuthorizationResult authorizeResult)
    {
        var clientId = context.User.FindFirst(ClaimNames.ClientId)?.Value;
        var hasClient = ClientIdRules.IsValid(clientId);
        var failed = authorizeResult.AuthorizationFailure?.FailedRequirements ?? [];
        var scopeRequirement = failed.OfType<ScopeRequirement>().FirstOrDefault();
        var provisioningFailed = failed.OfType<ProvisioningClientRequirement>().Any();

        var reason = ReasonFor(context, hasClient);
        var scope = hasClient ? scopeRequirement?.Scope : null;

        context.Items[AuthenticationEvents.DenialReasonItem] = reason;
        context.Response.Headers.WWWAuthenticate = ChallengeFor(scope);

        var route = RouteTemplates.Of(context);

        AuthLog.AuthorizationDenied(logger, reason, hasClient ? clientId : null, route, scope);

        if (hasClient && clientId is not null && IsWrite(context) && context.GetEndpoint() is RouteEndpoint endpoint)
        {
            auditor.Record(
                clientId,
                AccountOf(context),
                CorrelationIdMiddleware.Resolve(context),
                $"{context.Request.Method.ToUpperInvariant()} {RouteTemplates.Render(endpoint.RoutePattern)}",
                scopeRequirement is null && provisioningFailed
                    ? DeniedWriteReason.NotProvisioningClient
                    : DeniedWriteReason.InsufficientScope);
        }
    }
}
