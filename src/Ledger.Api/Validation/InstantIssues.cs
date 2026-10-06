namespace Ledger.Api.Validation;

internal static class InstantIssues
{
    public static ValidationIssue For(string field, InstantFailure failure) =>
        failure == InstantFailure.MissingTimeZone ? MissingTimeZone(field) : FieldIssues.InvalidInstant(field);

    public static ValidationIssue MissingTimeZone(string field) =>
        new(field, ValidationReasons.MissingTimeZone, $"Informe o fuso horário no campo '{field}', por exemplo 'Z' ou '-03:00'.");
}

internal enum InstantFailure
{
    None,
    Malformed,
    MissingTimeZone
}
