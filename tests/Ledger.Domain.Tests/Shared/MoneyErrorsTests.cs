using Ledger.Domain.Shared;
using Ledger.Domain.Tests.Support;

namespace Ledger.Domain.Tests.Shared;

[Trait("Category", "Unit")]
public sealed class MoneyErrorsTests
{
    public static TheoryData<Error, string, string, ErrorKind> Catalog => new()
    {
        {
            MoneyErrors.InvalidCurrency,
            "VALIDATION_FAILED",
            "A moeda deve ter três letras maiúsculas.",
            ErrorKind.Validation
        },
        {
            MoneyErrors.TooManyDecimals,
            "VALIDATION_FAILED",
            "O valor deve ter no máximo duas casas decimais.",
            ErrorKind.Validation
        },
        {
            MoneyErrors.OutOfRange,
            "VALIDATION_FAILED",
            "O valor está fora da faixa suportada.",
            ErrorKind.Validation
        },
        {
            MoneyErrors.MustBePositive,
            "VALIDATION_FAILED",
            "O valor deve ser maior que zero.",
            ErrorKind.Validation
        },
        {
            MoneyErrors.CurrencyMismatch,
            "CURRENCY_MISMATCH",
            "Os valores monetários devem ter a mesma moeda.",
            ErrorKind.Unprocessable
        }
    };

    [Theory]
    [MemberData(nameof(Catalog))]
    public void Error_HasTheCatalogCodeMessageAndKind(Error error, string code, string message, ErrorKind kind)
    {
        error.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        error.Kind.ShouldBe(kind);
    }

    [Fact]
    public void Catalog_ListsEveryErrorOfTheClass()
    {
        AllErrors().Count.ShouldBe(Catalog.Count);
    }

    [Fact]
    public void Errors_HaveDistinctMessages()
    {
        var messages = AllErrors().Select(error => error.Message).ToList();

        messages.Distinct(StringComparer.Ordinal).Count().ShouldBe(messages.Count);
    }

    [Fact]
    public void Errors_NeverCarryFormattingKeysQuotedValuesOrNumbers()
    {
        foreach (var error in AllErrors())
        {
            error.Message.ShouldNotContain("{");
            error.Message.ShouldNotContain("}");
            error.Message.ShouldNotContain("\"");
            error.Message.Any(char.IsAsciiDigit).ShouldBeFalse();
        }
    }

    [Fact]
    public void Errors_FollowTheMessageStyle()
    {
        foreach (var error in AllErrors())
        {
            MessageStyle.Violations(error.Message).ShouldBeEmpty(error.Message);
        }
    }

    private static List<Error> AllErrors() =>
        typeof(MoneyErrors).GetFields().Select(field => field.GetValue(null)).OfType<Error>().ToList();
}
