using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

public static class BalanceErrors
{
    public static readonly Error InvalidAsOf =
        new("INVALID_AS_OF",
            "O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.",
            ErrorKind.Validation);

    public static readonly Error AsOfInTheFuture =
        new("INVALID_AS_OF",
            "O parâmetro 'asOf' não pode ser posterior ao instante atual do ledger.",
            ErrorKind.Validation);
}
