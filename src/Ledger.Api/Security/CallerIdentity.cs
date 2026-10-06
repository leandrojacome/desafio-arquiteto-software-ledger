namespace Ledger.Api.Security;

internal static class CallerIdentity
{
    private const string ClientIdItem = "Ledger.ClientId";
    private const string CanWriteItem = "Ledger.CanWrite";

    public static string? ClientIdOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(ClientIdItem, out var cached))
        {
            return cached is string { Length: > 0 } text ? text : null;
        }

        var claimed = context.User.FindFirst(ClaimNames.ClientId)?.Value;
        var clientId = ClientIdRules.IsValid(claimed) ? claimed : null;

        context.Items[ClientIdItem] = clientId ?? string.Empty;

        return clientId;
    }

    public static bool CanWrite(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(CanWriteItem, out var cached) && cached is bool known)
        {
            return known;
        }

        var allowed = context.User.Identity is { IsAuthenticated: true }
                      && ClientIdOf(context) is not null
                      && ScopeClaims.Contains(context.User, AuthorizationPolicies.LedgerWrite);

        context.Items[CanWriteItem] = allowed;

        return allowed;
    }
}
