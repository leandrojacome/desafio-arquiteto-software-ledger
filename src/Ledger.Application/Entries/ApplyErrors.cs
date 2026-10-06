using Ledger.Domain.Shared;

namespace Ledger.Application.Entries;

public static class ApplyErrors
{
    public static readonly Error NotMatched =
        new("ENTRY_NOT_APPLIED",
            "A instrução condicional não encontrou nenhuma linha de conta correspondente.",
            ErrorKind.Unprocessable);
}
