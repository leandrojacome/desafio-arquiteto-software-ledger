namespace Ledger.Api.Security;

internal static class AuthFailureReasons
{
    public const string MissingToken = "missing_token";

    public const string InvalidToken = "invalid_token";

    public const string Expired = "expired";

    public const string InsufficientScope = "insufficient_scope";

    public const string MissingClientId = "missing_client_id";

    public const string InvalidClientId = "invalid_client_id";
}
