using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Ledger.Api.Security;

internal sealed class ProvisioningClientHandler(IOptions<ProvisioningOptions> options)
    : AuthorizationHandler<ProvisioningClientRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProvisioningClientRequirement requirement)
    {
        var clientId = context.User.FindFirst(ClaimNames.ClientId)?.Value;

        if (!string.IsNullOrWhiteSpace(clientId) && IsAllowed(options.Value.AccountProvisioningClients, clientId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static bool IsAllowed(string[] clients, string clientId)
    {
        return clients.Contains(ProvisioningOptions.Wildcard, StringComparer.Ordinal)
               || clients.Contains(clientId, StringComparer.Ordinal);
    }
}

internal sealed class ProvisioningClientRequirement : IAuthorizationRequirement;
