using System.Globalization;

namespace Ledger.Api.Validation;

internal static class FieldIssues
{
    public const string RootField = "$";

    public static ValidationIssue Required(string field) =>
        new(field, ValidationReasons.Required, $"O campo '{field}' é obrigatório.");

    public static ValidationIssue InvalidFormat(string field) =>
        new(field, ValidationReasons.InvalidFormat, $"O campo '{field}' tem formato inválido.");

    public static ValidationIssue InvalidEncoding(string field) =>
        new(field, ValidationReasons.InvalidFormat, $"O campo '{field}' deve estar em UTF-8 válido.");

    public static ValidationIssue InvalidAmount(string field) =>
        new(field, ValidationReasons.InvalidFormat,
            $"O campo '{field}' deve ser um texto decimal com ponto, como '80.00', sem símbolo de moeda nem separador de milhar.");

    public static ValidationIssue ControlCharacters(string field) =>
        new(field, ValidationReasons.InvalidFormat,
            $"O campo '{field}' não pode conter caracteres de controle, como quebra de linha ou tabulação.");

    public static ValidationIssue InvalidReference(string field) =>
        new(field, ValidationReasons.InvalidFormat,
            $"O campo '{field}' deve ter apenas caracteres ASCII visíveis, sem espaços nem acentos.");

    public static ValidationIssue InvalidInstant(string field) =>
        new(field, ValidationReasons.InvalidFormat,
            $"O campo '{field}' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo {Count(EntryRequestLimits.MaxFractionDigitsOfInstant, "casa decimal", "casas decimais")} de segundo.");

    public static ValidationIssue OutOfRange(string field) =>
        new(field, ValidationReasons.OutOfRange, $"O campo '{field}' está fora da faixa permitida.");

    public static ValidationIssue TooManyDecimals(string field) =>
        new(field, ValidationReasons.TooManyDecimals,
            $"O campo '{field}' deve ter no máximo {Count(EntryRequestLimits.MaxDecimalPlaces, "casa decimal", "casas decimais")}.");

    public static ValidationIssue TooLong(string field, int maxLength) =>
        new(field, ValidationReasons.TooLong,
            $"O campo '{field}' deve ter no máximo {Count(maxLength, "caractere", "caracteres")}.");

    public static ValidationIssue NotAllowed(string field) =>
        new(field, ValidationReasons.NotAllowed, $"O campo '{field}' não é um dos valores permitidos.");

    public static ValidationIssue UnsupportedCurrency(string field) =>
        new(field, ValidationReasons.UnsupportedCurrency, $"O campo '{field}' contém uma moeda não suportada.");

    public static ValidationIssue InTheFuture(string field, int toleranceMinutes)
    {
        var message = toleranceMinutes <= 0
            ? $"O campo '{field}' não pode estar à frente do relógio do servidor."
            : $"O campo '{field}' não pode estar mais de {Count(toleranceMinutes, "minuto", "minutos")} à frente do relógio do servidor.";

        return new ValidationIssue(field, ValidationReasons.InTheFuture, message);
    }

    public static ValidationIssue UnknownField(string field) =>
        new(field, ValidationReasons.UnknownField, "A propriedade não faz parte do contrato.");

    public static ValidationIssue InvalidJson() =>
        new(RootField, ValidationReasons.InvalidJson, "O corpo da requisição deve ser um único objeto JSON.");

    private static string Count(int value, string singular, string plural) =>
        string.Create(CultureInfo.InvariantCulture, $"{value} {(value == 1 ? singular : plural)}");
}
