namespace Ledger.Api.Validation;

internal sealed record ValidationIssue(string Field, string Reason, string Message);
