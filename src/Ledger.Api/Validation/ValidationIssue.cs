namespace Ledger.Api.Validation;

internal sealed record ValidationIssue(string Field, string Reason, string Message);

internal static class ValidationReasons
{
    public const string Required = "REQUIRED";

    public const string InvalidFormat = "INVALID_FORMAT";

    public const string InvalidJson = "INVALID_JSON";

    public const string UnknownField = "UNKNOWN_FIELD";

    public const string OutOfRange = "OUT_OF_RANGE";

    public const string TooManyDecimals = "TOO_MANY_DECIMALS";

    public const string TooLong = "TOO_LONG";

    public const string NotAllowed = "NOT_ALLOWED";

    public const string UnsupportedCurrency = "UNSUPPORTED_CURRENCY";

    public const string InTheFuture = "IN_THE_FUTURE";

    public const string MissingTimeZone = "MISSING_TIME_ZONE";

    public const string InvalidCursor = "INVALID_CURSOR";

    public const string FromAfterTo = "FROM_AFTER_TO";
}
