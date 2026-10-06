using Ledger.Domain.Shared;

namespace Ledger.Domain.Entries;

public static class EntryErrors
{
    public static readonly Error NotFound =
        new("ENTRY_NOT_FOUND", "O lançamento não existe nesta conta.", ErrorKind.NotFound);

    public static readonly Error InsufficientFunds =
        new("INSUFFICIENT_FUNDS",
            "O saldo resultante ficaria abaixo do limite de cheque especial.",
            ErrorKind.Unprocessable);

    public static readonly Error AlreadyReversed =
        new("ENTRY_ALREADY_REVERSED", "O lançamento já foi estornado.", ErrorKind.Conflict);

    public static readonly Error NotReversible =
        new("ENTRY_NOT_REVERSIBLE",
            "Não é possível estornar um lançamento que já é um estorno.",
            ErrorKind.Unprocessable);

    public static readonly Error IdempotencyKeyRequired =
        new("IDEMPOTENCY_KEY_REQUIRED", "O cabeçalho 'Idempotency-Key' é obrigatório.", ErrorKind.Validation);

    public static readonly Error IdempotencyKeyMalformed =
        new("VALIDATION_FAILED",
            "O valor de 'Idempotency-Key' deve ter de 1 a 128 caracteres ASCII visíveis.",
            ErrorKind.Validation);

    public static readonly Error IdempotencyKeyReused =
        new("IDEMPOTENCY_KEY_REUSED",
            "A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta.",
            ErrorKind.Unprocessable);

    public static readonly Error InvalidDescription =
        new("VALIDATION_FAILED",
            "A descrição não pode exceder o tamanho máximo nem conter caracteres de controle.",
            ErrorKind.Validation);

    public static readonly Error InvalidReference =
        new("VALIDATION_FAILED",
            "A referência deve ter apenas caracteres ASCII visíveis e não pode exceder o tamanho máximo.",
            ErrorKind.Validation);
}
