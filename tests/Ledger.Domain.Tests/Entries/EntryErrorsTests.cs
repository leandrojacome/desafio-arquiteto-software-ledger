using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Domain.Tests.Support;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryErrorsTests
{
    public static TheoryData<Error, string, ErrorKind> Catalog => new()
    {
        { EntryErrors.NotFound, "ENTRY_NOT_FOUND", ErrorKind.NotFound },
        { EntryErrors.InsufficientFunds, "INSUFFICIENT_FUNDS", ErrorKind.Unprocessable },
        { EntryErrors.AlreadyReversed, "ENTRY_ALREADY_REVERSED", ErrorKind.Conflict },
        { EntryErrors.NotReversible, "ENTRY_NOT_REVERSIBLE", ErrorKind.Unprocessable },
        { EntryErrors.IdempotencyKeyRequired, "IDEMPOTENCY_KEY_REQUIRED", ErrorKind.Validation },
        { EntryErrors.IdempotencyKeyMalformed, "VALIDATION_FAILED", ErrorKind.Validation },
        { EntryErrors.IdempotencyKeyReused, "IDEMPOTENCY_KEY_REUSED", ErrorKind.Unprocessable },
        { EntryErrors.InvalidDescription, "VALIDATION_FAILED", ErrorKind.Validation },
        { EntryErrors.InvalidReference, "VALIDATION_FAILED", ErrorKind.Validation }
    };

    public static TheoryData<Error, string> Messages => new()
    {
        { EntryErrors.NotFound, "O lançamento não existe nesta conta." },
        { EntryErrors.InsufficientFunds, "O saldo resultante ficaria abaixo do limite de cheque especial." },
        { EntryErrors.AlreadyReversed, "O lançamento já foi estornado." },
        { EntryErrors.NotReversible, "Não é possível estornar um lançamento que já é um estorno." },
        { EntryErrors.IdempotencyKeyRequired, "O cabeçalho 'Idempotency-Key' é obrigatório." },
        {
            EntryErrors.IdempotencyKeyMalformed,
            "O valor de 'Idempotency-Key' deve ter de 1 a 128 caracteres ASCII visíveis."
        },
        {
            EntryErrors.IdempotencyKeyReused,
            "A chave de idempotência já foi usada com um corpo de requisição diferente nesta conta."
        },
        {
            EntryErrors.InvalidDescription,
            "A descrição não pode exceder o tamanho máximo nem conter caracteres de controle."
        },
        {
            EntryErrors.InvalidReference,
            "A referência deve ter apenas caracteres ASCII visíveis e não pode exceder o tamanho máximo."
        }
    };

    [Theory]
    [MemberData(nameof(Catalog))]
    public void Error_HasTheCatalogCodeAndKind(Error error, string code, ErrorKind kind)
    {
        error.Code.ShouldBe(code);
        error.Kind.ShouldBe(kind);
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
    public void LimitsOtherThanTheKeyLength_AreNotWrittenAsNumbers()
    {
        EntryErrors.InvalidDescription.Message.Any(char.IsAsciiDigit).ShouldBeFalse();
        EntryErrors.InvalidReference.Message.Any(char.IsAsciiDigit).ShouldBeFalse();
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
    public void Errors_NeverCarryFormattingKeysOrQuotedValues()
    {
        foreach (var error in AllErrors())
        {
            error.Message.ShouldNotContain("{");
            error.Message.ShouldNotContain("}");
            error.Message.ShouldNotContain("\"");
            error.Message.ShouldEndWith(".");
        }
    }

    [Fact]
    public void IdempotencyKeyMalformed_StatesTheLimitAndNothingAboutTheValue()
    {
        EntryErrors.IdempotencyKeyMalformed.Message.ShouldContain("128");
    }

    private static List<Error> AllErrors() =>
        typeof(EntryErrors).GetFields().Select(field => field.GetValue(null)).OfType<Error>().ToList();
}
