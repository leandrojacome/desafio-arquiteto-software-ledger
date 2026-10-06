using System.Reflection;
using Ledger.Api.ErrorHandling;
using Ledger.Application;
using Ledger.Domain.Shared;
using Microsoft.AspNetCore.Http;

namespace Ledger.Api.IntegrationTests.ErrorHandling;

[Trait("Category", "Unit")]
public sealed class ProblemCatalogTests
{
    private static readonly string[] CatalogCodes =
    [
        "VALIDATION_FAILED", "IDEMPOTENCY_KEY_REQUIRED", "INVALID_AS_OF", "UNAUTHENTICATED", "FORBIDDEN",
        "ACCOUNT_NOT_FOUND", "ENTRY_NOT_FOUND", "ENTRY_ALREADY_REVERSED", "INSUFFICIENT_FUNDS", "CURRENCY_MISMATCH",
        "IDEMPOTENCY_KEY_REUSED", "ENTRY_NOT_REVERSIBLE", "RATE_LIMITED", "INTERNAL_ERROR", "SERVICE_UNAVAILABLE",
        "NOT_FOUND", "METHOD_NOT_ALLOWED", "PAYLOAD_TOO_LARGE", "UNSUPPORTED_MEDIA_TYPE"
    ];

    private static readonly string[] InternalSignals = ["ApplyErrors"];

    [Fact]
    public void TheCatalog_HoldsExactlyTheCodesOfTheContract()
    {
        ProblemCatalog.Codes.OrderBy(code => code, StringComparer.Ordinal)
            .ShouldBe(CatalogCodes.OrderBy(code => code, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "VALIDATION_FAILED")]
    [InlineData(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED")]
    [InlineData(StatusCodes.Status403Forbidden, "FORBIDDEN")]
    [InlineData(StatusCodes.Status404NotFound, "NOT_FOUND")]
    [InlineData(StatusCodes.Status405MethodNotAllowed, "METHOD_NOT_ALLOWED")]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE")]
    [InlineData(StatusCodes.Status415UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE")]
    [InlineData(StatusCodes.Status429TooManyRequests, "RATE_LIMITED")]
    [InlineData(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR")]
    [InlineData(StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE")]
    public void AKnownStatus_MapsToItsCatalogCode(int status, string expected)
    {
        ProblemCatalog.CodeFor(status).ShouldBe(expected);
    }

    [Theory]
    [InlineData(StatusCodes.Status406NotAcceptable)]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status414UriTooLong)]
    [InlineData(StatusCodes.Status422UnprocessableEntity)]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge)]
    public void AnUnknownClientError_BecomesAClientCodeAndNeverAnInternalError(int status)
    {
        var code = ProblemCatalog.CodeFor(status);

        code.ShouldBe("VALIDATION_FAILED");
        code.ShouldNotBe("INTERNAL_ERROR");
    }

    [Theory]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status501NotImplemented)]
    [InlineData(StatusCodes.Status502BadGateway)]
    [InlineData(StatusCodes.Status504GatewayTimeout)]
    public void OnlyAServerErrorOtherThan503_BecomesAnInternalError(int status)
    {
        ProblemCatalog.CodeFor(status).ShouldBe("INTERNAL_ERROR");
    }

    [Fact]
    public void EveryCode_HasATitleAndADetailInPortugueseThatEchoNoValue()
    {
        foreach (var code in ProblemCatalog.Codes)
        {
            ProblemCatalog.TitleFor(code).ShouldNotBeNullOrWhiteSpace(code);
            ProblemCatalog.TitleFor(code).ShouldNotEndWith(".", customMessage: code);
            ProblemCatalog.DetailFor(code).ShouldNotBeNullOrWhiteSpace(code);
            ProblemCatalog.DetailFor(code).ShouldNotContain("{", customMessage: code);
            ProblemCatalog.DetailFor(code).ShouldNotContain("\"", customMessage: code);
            ProblemCatalog.DetailFor(code).ShouldEndWith(".", customMessage: code);
        }
    }

    [Fact]
    public void EveryCode_CarriesTheTitleAndTheDetailThatTheCallerReads()
    {
        var expected = new Dictionary<string, (string Title, string Detail)>(StringComparer.Ordinal)
        {
            ["VALIDATION_FAILED"] = ("Falha na validação da requisição", "Um ou mais campos são inválidos."),
            ["IDEMPOTENCY_KEY_REQUIRED"] = (
                "Cabeçalho 'Idempotency-Key' obrigatório",
                "O cabeçalho 'Idempotency-Key' é obrigatório para esta requisição."),
            ["INVALID_AS_OF"] = (
                "Instante 'asOf' inválido",
                "O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', ter no máximo 6 casas decimais de segundo e não estar no futuro."),
            ["UNAUTHENTICATED"] = ("Autenticação necessária", "É necessária autenticação para acessar este recurso."),
            ["FORBIDDEN"] = ("Acesso negado", "O token não concede acesso a este recurso."),
            ["ACCOUNT_NOT_FOUND"] = ("Conta não encontrada", "A conta não existe."),
            ["ENTRY_NOT_FOUND"] = ("Lançamento não encontrado", "O lançamento não existe nesta conta."),
            ["ENTRY_ALREADY_REVERSED"] = ("Lançamento já estornado", "O lançamento já foi estornado."),
            ["INSUFFICIENT_FUNDS"] = (
                "Saldo insuficiente",
                "O saldo resultante ficaria abaixo do limite de cheque especial."),
            ["CURRENCY_MISMATCH"] = (
                "Moeda diferente da moeda da conta",
                "A moeda da requisição não corresponde à moeda da conta."),
            ["IDEMPOTENCY_KEY_REUSED"] = (
                "Chave de idempotência reutilizada em outra requisição",
                "A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta."),
            ["ENTRY_NOT_REVERSIBLE"] = (
                "Lançamento não pode ser estornado",
                "Não é possível estornar este lançamento."),
            ["RATE_LIMITED"] = (
                "Limite de requisições excedido",
                "A cota de requisições foi excedida. Tente novamente depois do intervalo informado no cabeçalho 'Retry-After'."),
            ["INTERNAL_ERROR"] = (
                "Erro interno",
                "Ocorreu um erro inesperado. Informe o 'correlationId' ao relatar o problema."),
            ["SERVICE_UNAVAILABLE"] = (
                "Serviço temporariamente indisponível",
                "Uma dependência necessária não respondeu a tempo. A requisição pode ser repetida."),
            ["NOT_FOUND"] = ("Recurso não encontrado", "O recurso solicitado não existe."),
            ["METHOD_NOT_ALLOWED"] = ("Método não permitido", "O método HTTP não é suportado para este recurso."),
            ["PAYLOAD_TOO_LARGE"] = (
                "Corpo da requisição grande demais",
                "O corpo da requisição excede o limite de 16 KiB."),
            ["UNSUPPORTED_MEDIA_TYPE"] = (
                "Tipo de mídia não suportado",
                "O 'Content-Type' deve ser 'application/json'.")
        };

        expected.Keys.Order(StringComparer.Ordinal).ShouldBe(ProblemCatalog.Codes.Order(StringComparer.Ordinal));

        foreach (var (code, texts) in expected)
        {
            ProblemCatalog.TitleFor(code).ShouldBe(texts.Title, code);
            ProblemCatalog.DetailFor(code).ShouldBe(texts.Detail, code);
        }
    }

    [Theory]
    [InlineData("INSUFFICIENT_FUNDS", "https://ledger.bank.internal/problems/insufficient-funds")]
    [InlineData("IDEMPOTENCY_KEY_REUSED", "https://ledger.bank.internal/problems/idempotency-key-reused")]
    [InlineData("NOT_FOUND", "https://ledger.bank.internal/problems/not-found")]
    public void TheType_IsTheLowerCaseCodeWithHyphens(string code, string expected)
    {
        ProblemCatalog.TypeFor(code).ShouldBe(expected);
    }

    [Fact]
    public void AnUnknownCode_FallsBackToTheInternalErrorTexts()
    {
        ProblemCatalog.TitleFor("NOT_IN_THE_CATALOG").ShouldBe(ProblemCatalog.TitleFor("INTERNAL_ERROR"));
        ProblemCatalog.DetailFor("NOT_IN_THE_CATALOG").ShouldBe(ProblemCatalog.DetailFor("INTERNAL_ERROR"));
        ProblemCatalog.Contains("NOT_IN_THE_CATALOG").ShouldBeFalse();
    }

    [Fact]
    public void EveryBusinessErrorOfTheDomain_AnswersWithTheStatusOfTheContract()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["VALIDATION_FAILED"] = 400,
            ["IDEMPOTENCY_KEY_REQUIRED"] = 400,
            ["INVALID_AS_OF"] = 400,
            ["ACCOUNT_NOT_FOUND"] = 404,
            ["ENTRY_NOT_FOUND"] = 404,
            ["ENTRY_ALREADY_REVERSED"] = 409,
            ["INSUFFICIENT_FUNDS"] = 422,
            ["CURRENCY_MISMATCH"] = 422,
            ["IDEMPOTENCY_KEY_REUSED"] = 422,
            ["ENTRY_NOT_REVERSIBLE"] = 422
        };

        var errors = typeof(Error).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(Error))
            .Select(field => (Error)field.GetValue(null)!)
            .ToList();

        errors.ShouldNotBeEmpty();

        foreach (var error in errors)
        {
            expected.ShouldContainKey(error.Code);
            ProblemFactory.FromError(new DefaultHttpContext(), error).Status
                .ShouldBe(expected[error.Code], error.Code);
        }
    }

    [Fact]
    public void EveryBusinessErrorOfTheDomainAndTheApplication_IsInTheCatalog()
    {
        var codes = new[] { typeof(Error).Assembly, ApplicationAssembly.Reference }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && type.IsAbstract && type.IsSealed && type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .Where(type => !InternalSignals.Contains(type.Name))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(Error))
            .Select(field => ((Error)field.GetValue(null)!).Code)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        codes.ShouldNotBeEmpty();
        codes.Where(code => !ProblemCatalog.Contains(code)).ShouldBeEmpty();
    }
}
