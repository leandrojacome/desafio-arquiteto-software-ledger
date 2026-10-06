using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Ledger.Application.Accounts;

namespace Ledger.Api.Contracts;

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
