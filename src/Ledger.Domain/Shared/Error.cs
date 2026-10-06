namespace Ledger.Domain.Shared;

public sealed record Error(string Code, string Message, ErrorKind Kind);

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unprocessable
}
