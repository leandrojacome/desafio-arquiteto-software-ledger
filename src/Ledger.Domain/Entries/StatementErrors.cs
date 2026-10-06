using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public static class StatementErrors
{
    public static readonly Error InvalidCursor =
        new("VALIDATION_FAILED", "O cursor não é válido para esta conta.", ErrorKind.Validation);
}
