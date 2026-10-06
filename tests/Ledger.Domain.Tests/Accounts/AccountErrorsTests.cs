using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Domain.Tests.Support;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class AccountErrorsTests
{
    public static TheoryData<Error, string, ErrorKind> Catalog => new()
    {
        { AccountErrors.NotFound, "ACCOUNT_NOT_FOUND", ErrorKind.NotFound },
        { AccountErrors.CurrencyMismatch, "CURRENCY_MISMATCH", ErrorKind.Unprocessable },
        { AccountErrors.InvalidHolderDocument, "VALIDATION_FAILED", ErrorKind.Validation },
        { AccountErrors.UnsupportedCurrency, "VALIDATION_FAILED", ErrorKind.Validation },
        { AccountErrors.InvalidOverdraftLimit, "VALIDATION_FAILED", ErrorKind.Validation },
        { AccountErrors.BalanceBelowOverdraftLimit, "VALIDATION_FAILED", ErrorKind.Validation },
        { AccountErrors.CreationKeyReused, "IDEMPOTENCY_KEY_REUSED", ErrorKind.Unprocessable }
    };

    public static TheoryData<Error, string> Messages => new()
    {
        { AccountErrors.NotFound, "A conta não existe." },
        { AccountErrors.CurrencyMismatch, "A moeda não corresponde à moeda da conta." },
        { AccountErrors.InvalidHolderDocument, "O documento do titular deve ser um CPF ou CNPJ válido." },
        { AccountErrors.UnsupportedCurrency, "A moeda não é suportada para novas contas." },
        {
            AccountErrors.InvalidOverdraftLimit,
            "O limite de cheque especial não pode ser negativo nem superior ao máximo permitido."
        },
        {
            AccountErrors.BalanceBelowOverdraftLimit,
            "O saldo não pode ficar abaixo do limite de cheque especial."
        },
        {
            AccountErrors.CreationKeyReused,
            "A chave de idempotência já foi usada com um corpo de requisição diferente por este chamador."
        }
    };

    [Theory]
    [MemberData(nameof(Catalog))]
    public void Error_HasTheCatalogCodeAndKind(Error error, string code, ErrorKind kind)
    {
        error.Code.ShouldBe(code);
        error.Kind.ShouldBe(kind);
    }

    [Fact]
    public void Catalog_ListsEveryErrorOfTheClass()
    {
        AllErrors().Count.ShouldBe(Catalog.Count);
    }

    [Theory]
    [MemberData(nameof(Messages))]
    public void Error_HasTheMessageInPortuguese(Error error, string message)
    {
        error.Message.ShouldBe(message);
    }

    [Fact]
    public void Messages_ListsEveryErrorOfTheClass()
    {
        AllErrors().Count.ShouldBe(Messages.Count);
    }

    [Fact]
    public void Errors_FollowTheMessageStyle()
    {
        foreach (var error in AllErrors())
        {
            MessageStyle.Violations(error.Message).ShouldBeEmpty(error.Message);
        }
    }

    [Fact]
    public void InvalidHolderDocumentAndUnsupportedCurrency_HaveDifferentMessages()
    {
        AccountErrors.InvalidHolderDocument.Message.ShouldNotBe(AccountErrors.UnsupportedCurrency.Message);
    }

    [Fact]
    public void Errors_HaveDistinctMessages()
    {
        var messages = AllErrors().Select(error => error.Message).ToList();

        messages.Distinct(StringComparer.Ordinal).Count().ShouldBe(messages.Count);
    }

    [Fact]
    public void Errors_NeverCarryFormattingKeysOrQuotedValues()
    {
        foreach (var error in AllErrors())
        {
            error.Message.ShouldNotContain("{");
            error.Message.ShouldNotContain("}");
            error.Message.ShouldNotContain("\"");
            error.Message.ShouldEndWith(".");
            error.Message.Any(char.IsAsciiDigit).ShouldBeFalse();
        }
    }

    private static List<Error> AllErrors() =>
        typeof(AccountErrors).GetFields().Select(field => field.GetValue(null)).OfType<Error>().ToList();
}
