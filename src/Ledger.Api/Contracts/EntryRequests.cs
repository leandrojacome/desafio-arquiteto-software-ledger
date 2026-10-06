using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.Contracts;

[Description("Corpo de POST /v1/accounts/{accountId}/entries. Propriedades desconhecidas são recusadas.")]
internal sealed record RegisterEntryRequest(
    [property: Description("CREDIT soma ao saldo e DEBIT subtrai dele.")]
    string Type,
    [property: Description("Valor positivo como texto decimal com no máximo duas casas decimais, até 999999999.99.")]
    [property: RegularExpression(ContractPatterns.Decimal)]
    string Amount,
    [property: Description("Código ISO 4217 do lançamento. Deve corresponder à moeda da conta.")]
    [property: RegularExpression(ContractPatterns.Currency)]
    string Currency,
    [property: Description("Data de negócio do lançamento, como instante ISO 8601 com fuso horário informado, por exemplo Z ou -03:00 (horário de Brasília, sem horário de verão desde 2019). Não pode estar no futuro além da tolerância do relógio do servidor, não pode ser anterior a 1970-01-01T00:00:00Z e não define o saldo. Se ausente, vale o instante do registro.")]
    [property: RegularExpression(ContractPatterns.Instant)]
    string? OccurredAt = null,
    [property: Description("Texto livre de até 140 caracteres, sem caracteres de controle.")]
    [property: StringLength(140)]
    string? Description = null,
    [property: Description("Referência do chamador com até 100 caracteres ASCII visíveis.")]
    [property: RegularExpression(ContractPatterns.VisibleAscii)]
    [property: StringLength(100)]
    string? Reference = null);

[Description("Corpo opcional da rota de estorno. Propriedades desconhecidas são recusadas.")]
internal sealed record ReverseEntryRequest(
    [property: Description("Texto livre de até 140 caracteres, sem caracteres de controle.")]
    [property: StringLength(140)]
    string? Description = null);
