using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Ledger.Application.Accounts;

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

[Description("Conta criada, ou a conta original quando a requisição é uma repetição.")]
internal sealed record CreateAccountResponse(
    [property: Description("Identificador da conta.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string AccountId,
    [property: Description("Código ISO 4217 da conta.")]
    string Currency,
    [property: Description("Limite de cheque especial como texto decimal com duas casas decimais.")]
    [property: RegularExpression(ContractPatterns.SignedDecimal)]
    string OverdraftLimit,
    [property: Description("Documento do titular com todos os dígitos ocultos, exceto os que a máscara deixa visíveis.")]
    string HolderDocumentMasked,
    [property: Description("Instante em que a conta foi criada, em UTC.")]
    [property: RegularExpression(ContractPatterns.ResponseInstant)]
    string CreatedAt)
{
    public static CreateAccountResponse From(CreatedAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new CreateAccountResponse(
            account.AccountId.ToString(),
            account.Currency,
            account.OverdraftLimit.ToDecimalString(),
            account.HolderDocumentMasked,
            InstantText.From(account.CreatedAt));
    }
}
