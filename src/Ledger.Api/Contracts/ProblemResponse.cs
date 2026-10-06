using System.ComponentModel;

namespace Ledger.Api.Contracts;

[Description("Problem Details (RFC 9457) devolvido em toda resposta de erro.")]
internal sealed record ProblemResponse(
    [property: Description("URI que identifica o problema. É derivada do código.")]
    string Type,
    [property: Description("Resumo curto do problema. É fixo para cada código.")]
    string Title,
    [property: Description("Status HTTP da resposta.")]
    int Status,
    [property: Description("Explicação do problema, fixa para cada situação. Nunca repete valores recebidos.")]
    string Detail,
    [property: Description("Caminho da requisição.")]
    string Instance,
    [property: Description("Código de erro estável do catálogo.")]
    string Code,
    [property: Description("Identificador que acompanha a requisição nos logs. Repete o X-Correlation-Id.")]
    string CorrelationId,
    [property: Description("Identificador do trace da requisição.")]
    string TraceId,
    [property: Description("Campos inválidos. Presente apenas em VALIDATION_FAILED.")]
    IReadOnlyList<ProblemFieldError>? Errors = null);

[Description("Um campo inválido de uma requisição.")]
internal sealed record ProblemFieldError(
    [property: Description("Nome da propriedade do corpo, do parâmetro de consulta ou do cabeçalho, ou $ para o corpo inteiro.")]
    string Field,
    [property: Description("Código estável do motivo, como REQUIRED, INVALID_FORMAT, OUT_OF_RANGE ou TOO_LONG.")]
    string Reason,
    [property: Description("Frase fixa que nunca repete o valor recebido.")]
    string Message);
