using Microsoft.AspNetCore.Authorization;

namespace Ledger.Api.Security;

internal sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var hasClientId = ClientIdRules.IsValid(context.User.FindFirst(ClaimNames.ClientId)?.Value);

        if (hasClientId && ScopeClaims.Contains(context.User, requirement.Scope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
