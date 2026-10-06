using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Ledger.Application.Entries;
using Ledger.Domain.Entries;

namespace Ledger.Api.Contracts;

[Description("Lançamento do ledger. Os lançamentos são imutáveis: um erro se corrige com um estorno.")]
internal sealed record EntryResponse(
    [property: Description("Identificador do lançamento.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string EntryId,
    [property: Description("Identificador da conta.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string AccountId,
    [property: Description("Número de sequência do lançamento na conta, a partir de 1.")]
    long AccountVersion,
    [property: Description("CREDIT ou DEBIT.")]
    string Type,
    [property: Description("Valor positivo como texto decimal com duas casas decimais.")]
    [property: RegularExpression(ContractPatterns.Decimal)]
    string Amount,
    [property: Description("Código ISO 4217 do lançamento.")]
    string Currency,
    [property: Description("Saldo da conta logo depois deste lançamento.")]
    [property: RegularExpression(ContractPatterns.SignedDecimal)]
    string BalanceAfter,
    [property: Description("Data de negócio informada pelo chamador, convertida para UTC.")]
    [property: RegularExpression(ContractPatterns.ResponseInstant)]
    string OccurredAt,
    [property: Description("Instante em que o ledger registrou o lançamento, em UTC. Define o saldo em um instante.")]
    [property: RegularExpression(ContractPatterns.ResponseInstant)]
    string RecordedAt,
    [property: Description("Lançamento que este estorna, ou null.")]
    [property: RegularExpression(ContractPatterns.Uuid)]
    string? ReversesEntryId,
    [property: Description("Texto livre informado pelo chamador, ou null.")]
    string? Description,
    [property: Description("Referência informada pelo chamador, ou null.")]
    string? Reference)
{
    public static EntryResponse From(EntryView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new EntryResponse(
            view.Id.ToString(),
            view.AccountId.ToString(),
            view.AccountVersion,
            view.Type.ToDatabaseText(),
            view.Amount.ToDecimalString(),
            view.Amount.Currency,
            view.BalanceAfter.ToDecimalString(),
            InstantText.From(view.OccurredAt),
            InstantText.From(view.RecordedAt),
            view.ReversesEntryId?.ToString(),
            view.Description,
            view.Reference);
    }
}
