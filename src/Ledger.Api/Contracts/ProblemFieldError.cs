using System.ComponentModel;

namespace Ledger.Api.Contracts;

[Description("Um campo inválido de uma requisição.")]
internal sealed record ProblemFieldError(
    [property: Description("Nome da propriedade do corpo, do parâmetro de consulta ou do cabeçalho, ou $ para o corpo inteiro.")]
    string Field,
    [property: Description("Código estável do motivo, como REQUIRED, INVALID_FORMAT, OUT_OF_RANGE ou TOO_LONG.")]
    string Reason,
    [property: Description("Frase fixa que nunca repete o valor recebido.")]
    string Message);
