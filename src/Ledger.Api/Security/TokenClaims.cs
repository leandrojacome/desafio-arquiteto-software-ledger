using System.Security.Claims;

namespace Ledger.Api.Security;

internal static class ClaimNames
{
    public const string Scope = "scope";

    public const string ClientId = "client_id";
}

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

internal static class ClientIdRules
{
    public const int MaxLength = 128;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '!' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
