using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Ledger.Api.Contracts;

[Description("Corpo opcional da rota de estorno. Propriedades desconhecidas são recusadas.")]
internal sealed record ReverseEntryRequest(
    [property: Description("Texto livre de até 140 caracteres, sem caracteres de controle.")]
    [property: StringLength(140)]
    string? Description = null);
