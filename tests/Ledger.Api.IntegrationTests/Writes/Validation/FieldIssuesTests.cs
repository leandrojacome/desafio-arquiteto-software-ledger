using Ledger.Api.Validation;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class FieldIssuesTests
{
    [Fact]
    public void Required_NamesTheFieldInSingleQuotes()
    {
        var issue = FieldIssues.Required("currency");

        issue.ShouldBe(new ValidationIssue("currency", "REQUIRED", "O campo 'currency' é obrigatório."));
    }

    [Fact]
    public void InvalidFormat_KeepsTheReasonAndSaysTheFormatIsInvalid()
    {
        var issue = FieldIssues.InvalidFormat("holderDocument");

        issue.ShouldBe(new ValidationIssue("holderDocument", "INVALID_FORMAT", "O campo 'holderDocument' tem formato inválido."));
    }

    [Theory]
    [InlineData("occurredAt")]
    [InlineData("from")]
    [InlineData("to")]
    public void InvalidInstant_AsksForTheTimeZoneWithBothExamples(string field)
    {
        var issue = FieldIssues.InvalidInstant(field);

        issue.Field.ShouldBe(field);
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            $"O campo '{field}' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("overdraftLimit")]
    public void InvalidAmount_ExplainsTheDecimalTextWithAPointAndWithoutCurrencyOrThousandsSeparator(string field)
    {
        var issue = FieldIssues.InvalidAmount(field);

        issue.Field.ShouldBe(field);
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            $"O campo '{field}' deve ser um texto decimal com ponto, como '80.00', sem símbolo de moeda nem separador de milhar.");
    }

    [Fact]
    public void InvalidReference_ExplainsThatOnlyVisibleAsciiWithoutSpacesOrAccentsIsAllowed()
    {
        var issue = FieldIssues.InvalidReference("reference");

        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'reference' deve ter apenas caracteres ASCII visíveis, sem espaços nem acentos.");
    }

    [Fact]
    public void ControlCharacters_ExplainsThatLineBreaksAndTabsAreNotAllowed()
    {
        var issue = FieldIssues.ControlCharacters("description");

        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            "O campo 'description' não pode conter caracteres de controle, como quebra de linha ou tabulação.");
    }

    [Fact]
    public void InvalidEncoding_ExplainsThatTheTextMustBeValidUtf8()
    {
        var issue = FieldIssues.InvalidEncoding("description");

        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'description' deve estar em UTF-8 válido.");
    }

    [Fact]
    public void OutOfRange_SaysTheValueIsOutsideTheAllowedRange()
    {
        FieldIssues.OutOfRange("amount").Message.ShouldBe("O campo 'amount' está fora da faixa permitida.");
    }

    [Fact]
    public void TooManyDecimals_UsesTheLimitOfTheContractInThePlural()
    {
        var issue = FieldIssues.TooManyDecimals("amount");

        issue.Reason.ShouldBe("TOO_MANY_DECIMALS");
        issue.Message.ShouldBe("O campo 'amount' deve ter no máximo 2 casas decimais.");
    }

    [Theory]
    [InlineData(1, "O campo 'description' deve ter no máximo 1 caractere.")]
    [InlineData(100, "O campo 'description' deve ter no máximo 100 caracteres.")]
    [InlineData(140, "O campo 'description' deve ter no máximo 140 caracteres.")]
    public void TooLong_ChoosesTheSingularOrThePluralOfCharacters(int maxLength, string expected)
    {
        var issue = FieldIssues.TooLong("description", maxLength);

        issue.Reason.ShouldBe("TOO_LONG");
        issue.Message.ShouldBe(expected);
    }

    [Fact]
    public void NotAllowed_SaysTheValueIsNotOneOfTheAllowedOnes()
    {
        FieldIssues.NotAllowed("type").Message.ShouldBe("O campo 'type' não é um dos valores permitidos.");
    }

    [Fact]
    public void UnsupportedCurrency_SaysTheCurrencyIsNotSupported()
    {
        var issue = FieldIssues.UnsupportedCurrency("currency");

        issue.Reason.ShouldBe("UNSUPPORTED_CURRENCY");
        issue.Message.ShouldBe("O campo 'currency' contém uma moeda não suportada.");
    }

    [Theory]
    [InlineData(0, "O campo 'occurredAt' não pode estar à frente do relógio do servidor.")]
    [InlineData(1, "O campo 'occurredAt' não pode estar mais de 1 minuto à frente do relógio do servidor.")]
    [InlineData(5, "O campo 'occurredAt' não pode estar mais de 5 minutos à frente do relógio do servidor.")]
    [InlineData(60, "O campo 'occurredAt' não pode estar mais de 60 minutos à frente do relógio do servidor.")]
    public void InTheFuture_ChoosesTheFormOfTheToleranceThatTheConfigurationAllows(int minutes, string expected)
    {
        var issue = FieldIssues.InTheFuture("occurredAt", minutes);

        issue.Reason.ShouldBe("IN_THE_FUTURE");
        issue.Message.ShouldBe(expected);
    }

    [Fact]
    public void UnknownField_CarriesTheNameAndAFixedSentenceThatNeverEchoesIt()
    {
        var issue = FieldIssues.UnknownField("clientId");

        issue.Field.ShouldBe("clientId");
        issue.Reason.ShouldBe("UNKNOWN_FIELD");
        issue.Message.ShouldBe("A propriedade não faz parte do contrato.");
    }

    [Fact]
    public void InvalidJson_IsAboutTheWholeBodyAndNeverCitesTheRootMarker()
    {
        var issue = FieldIssues.InvalidJson();

        issue.Field.ShouldBe("$");
        issue.Reason.ShouldBe("INVALID_JSON");
        issue.Message.ShouldBe("O corpo da requisição deve ser um único objeto JSON.");
        issue.Message.ShouldNotContain("$");
    }

    [Fact]
    public void EveryMessage_IsASentenceWithoutBracesDoubleQuotesOrDashes()
    {
        ValidationIssue[] issues =
        [
            FieldIssues.Required("amount"),
            FieldIssues.InvalidFormat("amount"),
            FieldIssues.InvalidInstant("occurredAt"),
            FieldIssues.InvalidAmount("amount"),
            FieldIssues.InvalidReference("reference"),
            FieldIssues.ControlCharacters("description"),
            FieldIssues.InvalidEncoding("description"),
            FieldIssues.OutOfRange("amount"),
            FieldIssues.TooManyDecimals("amount"),
            FieldIssues.TooLong("reference", 100),
            FieldIssues.NotAllowed("type"),
            FieldIssues.UnsupportedCurrency("currency"),
            FieldIssues.InTheFuture("occurredAt", 5),
            FieldIssues.UnknownField("other"),
            FieldIssues.InvalidJson()
        ];

        foreach (var issue in issues)
        {
            issue.Message.ShouldEndWith(".", customMessage: issue.Reason);
            char.IsUpper(issue.Message[0]).ShouldBeTrue(issue.Reason);
            issue.Message.ShouldNotContain("{", customMessage: issue.Reason);
            issue.Message.ShouldNotContain("}", customMessage: issue.Reason);
            issue.Message.ShouldNotContain("\"", customMessage: issue.Reason);
            issue.Message.ShouldNotContain("\u2014", customMessage: issue.Reason);
            issue.Message.ShouldNotContain("\u2013", customMessage: issue.Reason);
        }
    }
}
