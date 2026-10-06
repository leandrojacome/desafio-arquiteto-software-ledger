namespace Ledger.Domain.Shared;

public sealed record Error(string Code, string Message, ErrorKind Kind);
