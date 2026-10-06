using System.Collections.Frozen;
using System.Globalization;
using Ledger.Api.Validation;

namespace Ledger.Api.ErrorHandling;

internal static class ProblemCatalog
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string InvalidAsOf = "INVALID_AS_OF";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string AccountNotFound = "ACCOUNT_NOT_FOUND";
    public const string EntryNotFound = "ENTRY_NOT_FOUND";
    public const string EntryAlreadyReversed = "ENTRY_ALREADY_REVERSED";
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string EntryNotReversible = "ENTRY_NOT_REVERSIBLE";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";

    private const string TypeBase = "https://ledger.bank.internal/problems/";

    private static readonly string PayloadTooLargeDetail = string.Create(
        CultureInfo.InvariantCulture,
        $"O corpo da requisição excede o limite de {ApiConstants.MaxRequestBodyBytes / 1024} KiB.");

    private static readonly string InvalidAsOfDetail = string.Create(
        CultureInfo.InvariantCulture,
        $"O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', ter no máximo {EntryRequestLimits.MaxFractionDigitsOfInstant} casas decimais de segundo e não estar no futuro.");

    private static readonly FrozenDictionary<string, Entry> EntriesByCode = new Dictionary<string, Entry>
    {
        [ValidationFailed] = new("Falha na validação da requisição", "Um ou mais campos são inválidos."),
        [IdempotencyKeyRequired] = new(
            "Cabeçalho 'Idempotency-Key' obrigatório",
            "O cabeçalho 'Idempotency-Key' é obrigatório para esta requisição."),
        [InvalidAsOf] = new(
            "Instante 'asOf' inválido",
            InvalidAsOfDetail),
        [Unauthenticated] = new(
            "Autenticação necessária",
            "É necessária autenticação para acessar este recurso."),
        [Forbidden] = new("Acesso negado", "O token não concede acesso a este recurso."),
        [AccountNotFound] = new("Conta não encontrada", "A conta não existe."),
        [EntryNotFound] = new("Lançamento não encontrado", "O lançamento não existe nesta conta."),
        [EntryAlreadyReversed] = new("Lançamento já estornado", "O lançamento já foi estornado."),
        [InsufficientFunds] = new(
            "Saldo insuficiente",
            "O saldo resultante ficaria abaixo do limite de cheque especial."),
        [CurrencyMismatch] = new(
            "Moeda diferente da moeda da conta",
            "A moeda da requisição não corresponde à moeda da conta."),
        [IdempotencyKeyReused] = new(
            "Chave de idempotência reutilizada em outra requisição",
            "A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta."),
        [EntryNotReversible] = new("Lançamento não pode ser estornado", "Não é possível estornar este lançamento."),
        [RateLimited] = new(
            "Limite de requisições excedido",
            "A cota de requisições foi excedida. Tente novamente depois do intervalo informado no cabeçalho 'Retry-After'."),
        [InternalError] = new(
            "Erro interno",
            "Ocorreu um erro inesperado. Informe o 'correlationId' ao relatar o problema."),
        [ServiceUnavailable] = new(
            "Serviço temporariamente indisponível",
            "Uma dependência necessária não respondeu a tempo. A requisição pode ser repetida."),
        [NotFound] = new("Recurso não encontrado", "O recurso solicitado não existe."),
        [MethodNotAllowed] = new("Método não permitido", "O método HTTP não é suportado para este recurso."),
        [PayloadTooLarge] = new("Corpo da requisição grande demais", PayloadTooLargeDetail),
        [UnsupportedMediaType] = new(
            "Tipo de mídia não suportado",
            "O 'Content-Type' deve ser 'application/json'.")
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<int, string> CodesByStatus = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = ValidationFailed,
        [StatusCodes.Status401Unauthorized] = Unauthenticated,
        [StatusCodes.Status403Forbidden] = Forbidden,
        [StatusCodes.Status404NotFound] = NotFound,
        [StatusCodes.Status405MethodNotAllowed] = MethodNotAllowed,
        [StatusCodes.Status413PayloadTooLarge] = PayloadTooLarge,
        [StatusCodes.Status415UnsupportedMediaType] = UnsupportedMediaType,
        [StatusCodes.Status429TooManyRequests] = RateLimited,
        [StatusCodes.Status503ServiceUnavailable] = ServiceUnavailable
    }.ToFrozenDictionary();

    public static IReadOnlyCollection<string> Codes => EntriesByCode.Keys;

    public static bool Contains(string code) => EntriesByCode.ContainsKey(code);

    public static string TitleFor(string code) =>
        EntriesByCode.TryGetValue(code, out var entry) ? entry.Title : EntriesByCode[InternalError].Title;

    public static string DetailFor(string code) =>
        EntriesByCode.TryGetValue(code, out var entry) ? entry.Detail : EntriesByCode[InternalError].Detail;

    public static string CodeFor(int status)
    {
        if (CodesByStatus.TryGetValue(status, out var code))
        {
            return code;
        }

        return status is >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError
            ? ValidationFailed
            : InternalError;
    }

    public static string TypeFor(string code)
    {
        return TypeBase + string.Create(code.Length, code, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                span[index] = source[index] == '_' ? '-' : char.ToLowerInvariant(source[index]);
            }
        });
    }

    private sealed record Entry(string Title, string Detail);
}
