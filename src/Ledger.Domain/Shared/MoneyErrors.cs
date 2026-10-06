namespace Ledger.Domain.Shared;

public static class MoneyErrors
{
    public static readonly Error InvalidCurrency =
        new("VALIDATION_FAILED", "A moeda deve ter três letras maiúsculas.", ErrorKind.Validation);

    public static readonly Error TooManyDecimals =
        new("VALIDATION_FAILED", "O valor deve ter no máximo duas casas decimais.", ErrorKind.Validation);

    public static readonly Error OutOfRange =
        new("VALIDATION_FAILED", "O valor está fora da faixa suportada.", ErrorKind.Validation);

    public static readonly Error MustBePositive =
        new("VALIDATION_FAILED", "O valor deve ser maior que zero.", ErrorKind.Validation);

    public static readonly Error CurrencyMismatch =
        new("CURRENCY_MISMATCH", "Os valores monetários devem ter a mesma moeda.", ErrorKind.Unprocessable);
}
