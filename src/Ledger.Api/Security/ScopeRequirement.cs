using Microsoft.AspNetCore.Authorization;

namespace Ledger.Api.Security;

internal sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string Scope { get; } = scope;
}
