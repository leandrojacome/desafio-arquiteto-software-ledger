using System.Security.Claims;
using Ledger.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Security;

internal sealed class AuthenticationEvents(
    ISecurityTelemetry telemetry,
    IOptions<JwtAuthenticationOptions> authentication,
    ILogger<AuthenticationEvents> logger) : JwtBearerEvents
{
    public const string DenialReasonItem = "Ledger.AuthDenialReason";

    public const string InvalidClientIdItem = "Ledger.InvalidClientId";

    private const string InvalidTokenError = "invalid_token";

    public override Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ExceedsLifetimeCeiling(context.SecurityToken, authentication.Value.MaxTokenLifetimeMinutes))
        {
            if (!RouteTemplates.AllowsAnonymous(context.HttpContext))
            {
                Count(context.HttpContext, AuthFailureReasons.InvalidToken);
            }

            context.Fail("The token lifetime exceeds the accepted maximum.");

            return Task.CompletedTask;
        }

        if (context.Principal is { } principal && DropUnacceptableClientId(principal))
        {
            context.HttpContext.Items[InvalidClientIdItem] = true;
        }

        return Task.CompletedTask;
    }

    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RouteTemplates.AllowsAnonymous(context.HttpContext))
        {
            return Task.CompletedTask;
        }

        var reason = context.Exception is SecurityTokenExpiredException or SecurityTokenNotYetValidException
            ? AuthFailureReasons.Expired
            : AuthFailureReasons.InvalidToken;

        Count(context.HttpContext, reason);

        return Task.CompletedTask;
    }

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.AuthenticateFailure is not null)
        {
            context.Error = InvalidTokenError;

            return Task.CompletedTask;
        }

        if (!RouteTemplates.AllowsAnonymous(context.HttpContext))
        {
            Count(context.HttpContext, AuthFailureReasons.MissingToken);
        }

        return Task.CompletedTask;
    }

    public override Task Forbidden(ForbiddenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var reason = context.HttpContext.Items.TryGetValue(DenialReasonItem, out var stored) && stored is string text
            ? text
            : AuthFailureReasons.InsufficientScope;

        telemetry.AuthFailed(reason);

        return Task.CompletedTask;
    }

    private void Count(HttpContext context, string reason)
    {
        telemetry.AuthFailed(reason);

        if (logger.IsEnabled(LogLevel.Information))
        {
            var route = RouteTemplates.Of(context);
            AuthLog.AuthenticationRejected(logger, reason, route);
        }
    }

    private static bool DropUnacceptableClientId(ClaimsPrincipal principal)
    {
        var claims = principal.FindAll(ClaimNames.ClientId).ToList();

        if (claims.Count == 0 || (claims.Count == 1 && ClientIdRules.IsValid(claims[0].Value)))
        {
            return false;
        }

        foreach (var claim in claims)
        {
            claim.Subject?.TryRemoveClaim(claim);
        }

        return claims.Exists(claim => !string.IsNullOrWhiteSpace(claim.Value));
    }

    private static bool ExceedsLifetimeCeiling(SecurityToken token, int ceilingMinutes)
    {
        var expires = token.ValidTo;
        var start = EarliestStart(token);

        return expires == DateTime.MinValue
               || start == DateTime.MaxValue
               || expires - start > TimeSpan.FromMinutes(ceilingMinutes);
    }

    private static DateTime EarliestStart(SecurityToken token)
    {
        var start = DateTime.MaxValue;

        if (token.ValidFrom != DateTime.MinValue)
        {
            start = token.ValidFrom;
        }

        if (token is JsonWebToken { IssuedAt: var issuedAt } && issuedAt != DateTime.MinValue && issuedAt < start)
        {
            start = issuedAt;
        }

        return start;
    }
}
