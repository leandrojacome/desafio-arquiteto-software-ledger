using System.Security.Claims;

namespace Ledger.Api.Security;

internal static class ScopeClaims
{
    private static readonly char[] Separator = [' '];

    public static bool Contains(ClaimsPrincipal user, string scope)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (var claim in user.FindAll(ClaimNames.Scope))
        {
            foreach (var granted in claim.Value.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(granted, scope, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
