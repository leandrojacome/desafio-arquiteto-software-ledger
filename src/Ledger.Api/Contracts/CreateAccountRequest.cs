using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.Contracts;

[Description("Corpo de POST /v1/accounts. Propriedades desconhecidas são recusadas.")]
internal sealed record CreateAccountRequest(
    [property: Description("CPF ou CNPJ do titular, com ou sem pontuação, até 18 caracteres. É armazenado cifrado e nunca devolvido.")]
    [property: StringLength(18, MinimumLength = 11)]
    string HolderDocument,
    [property: Description("Código ISO 4217 da conta. Só BRL é aceito.")]
    [property: RegularExpression(ContractPatterns.Currency)]
    string Currency,
    [property: Description("Limite de cheque especial como texto decimal com no máximo duas casas decimais. O padrão é 0.00.")]
    [property: RegularExpression(ContractPatterns.Decimal)]
    string? OverdraftLimit = null);
