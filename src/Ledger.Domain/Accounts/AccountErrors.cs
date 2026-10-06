using Ledger.Domain.Shared;

namespace Ledger.Domain.Accounts;

public static class AccountErrors
{
    public static readonly Error NotFound =
        new("ACCOUNT_NOT_FOUND", "A conta não existe.", ErrorKind.NotFound);

    public static readonly Error CurrencyMismatch =
        new("CURRENCY_MISMATCH", "A moeda não corresponde à moeda da conta.", ErrorKind.Unprocessable);

    public static readonly Error InvalidHolderDocument =
        new("VALIDATION_FAILED", "O documento do titular deve ser um CPF ou CNPJ válido.", ErrorKind.Validation);

    public static readonly Error UnsupportedCurrency =
        new("VALIDATION_FAILED", "A moeda não é suportada para novas contas.", ErrorKind.Validation);

    public static readonly Error InvalidOverdraftLimit =
        new("VALIDATION_FAILED",
            "O limite de cheque especial não pode ser negativo nem superior ao máximo permitido.",
            ErrorKind.Validation);

    public static readonly Error BalanceBelowOverdraftLimit =
        new("VALIDATION_FAILED", "O saldo não pode ficar abaixo do limite de cheque especial.", ErrorKind.Validation);

    public static readonly Error CreationKeyReused =
        new("IDEMPOTENCY_KEY_REUSED",
            "A chave de idempotência já foi usada com um corpo de requisição diferente por este chamador.",
            ErrorKind.Unprocessable);
}

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
