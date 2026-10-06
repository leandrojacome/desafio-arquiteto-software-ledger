using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Ledger.Application.Balances;

namespace Ledger.Api.Contracts;

[Description("Saldo de uma conta agora ou em um instante.")]
internal sealed record BalanceResponse(
    [property: Description("Identificador da conta.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string AccountId,
    [property: Description("Código ISO 4217 da conta.")]
    string Currency,
    [property: Description("Saldo como texto decimal com sinal e duas casas decimais.")]
    [property: RegularExpression(ContractPatterns.SignedDecimal)]
    string Balance,
    [property: Description("Limite de cheque especial da conta, como texto decimal com duas casas decimais.")]
    [property: RegularExpression(ContractPatterns.SignedDecimal)]
    string OverdraftLimit,
    [property: Description("Instante a que o saldo se refere, em UTC. É o instante da requisição quando asOf está ausente.")]
    [property: RegularExpression(ContractPatterns.ResponseInstant)]
    string AsOf,
    [property: Description("Último lançamento registrado até o instante, ou null quando a conta ainda não tem lançamentos.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string? LastEntryId,
    [property: Description("Presente apenas quando asOf foi enviado. Indica se o instante já está fora da janela de acomodação, de modo que a resposta não pode mais mudar.")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Settled)
{
    public static BalanceResponse From(BalanceView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new BalanceResponse(
            view.AccountId.ToString(),
            view.Balance.Currency,
            view.Balance.ToDecimalString(),
            view.OverdraftLimit.ToDecimalString(),
            InstantText.From(view.AsOf),
            view.LastEntryId?.ToString(),
            view.Settled);
    }
}
