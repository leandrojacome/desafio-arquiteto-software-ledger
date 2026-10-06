namespace Ledger.Api.Contracts;

internal static class ContractPatterns
{
    public const string Decimal = "^[0-9]+(\\.[0-9]{1,2})?$";

    public const string SignedDecimal = "^-?[0-9]+\\.[0-9]{2}$";

    public const string Uuid = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

    public const string Instant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\\.([0-9]{1,6}|[0-9]{6}0{1,3}))?(Z|[+-][0-9]{2}:[0-9]{2})$";

    public const string ResponseInstant = "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{6}Z$";

    public const string Currency = "^[A-Z]{3}$";

    public const string VisibleAscii = "^[!-~]+$";
}
