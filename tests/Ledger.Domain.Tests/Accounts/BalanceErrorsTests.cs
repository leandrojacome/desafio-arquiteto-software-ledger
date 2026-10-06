using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Domain.Tests.Support;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class BalanceErrorsTests
{
    [Fact]
    public void InvalidAsOf_HasTheInvalidAsOfCodeAndIsAValidationError()
    {
        BalanceErrors.InvalidAsOf.Code.ShouldBe("INVALID_AS_OF");
        BalanceErrors.InvalidAsOf.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void AsOfInTheFuture_HasTheInvalidAsOfCodeAndIsAValidationError()
    {
        BalanceErrors.AsOfInTheFuture.Code.ShouldBe("INVALID_AS_OF");
        BalanceErrors.AsOfInTheFuture.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Messages_AreTheOnesOfTheReadContract()
    {
        BalanceErrors.InvalidAsOf.Message.ShouldBe(
            "O parâmetro 'asOf' deve ser um instante no padrão ISO 8601 com fuso horário, por exemplo 'Z' ou '-03:00', e ter no máximo 6 casas decimais de segundo.");
        BalanceErrors.AsOfInTheFuture.Message.ShouldBe(
            "O parâmetro 'asOf' não pode ser posterior ao instante atual do ledger.");
    }

    [Fact]
    public void Messages_AreDifferentFromEachOther()
    {
        BalanceErrors.InvalidAsOf.Message.ShouldNotBe(BalanceErrors.AsOfInTheFuture.Message);
        BalanceErrors.InvalidAsOf.ShouldNotBe(BalanceErrors.AsOfInTheFuture);
    }

    [Fact]
    public void Messages_HaveNoFormattingKeysAndNoQuotedValue()
    {
        foreach (var error in new[] { BalanceErrors.InvalidAsOf, BalanceErrors.AsOfInTheFuture })
        {
            error.Message.ShouldNotContain("{");
            error.Message.ShouldNotContain("}");
            error.Message.ShouldNotContain("\"");
        }
    }

    [Fact]
    public void InvalidAsOf_TellsThatTheTimeZoneIsRequiredAndGivesTheTwoExamples()
    {
        BalanceErrors.InvalidAsOf.Message.ShouldContain("fuso horário");
        BalanceErrors.InvalidAsOf.Message.ShouldContain("'Z'");
        BalanceErrors.InvalidAsOf.Message.ShouldContain("'-03:00'");
    }

    [Fact]
    public void InvalidAsOf_TellsTheMostDecimalPlacesOfTheSecondThatItAccepts()
    {
        BalanceErrors.InvalidAsOf.Message.ShouldContain("6 casas decimais de segundo");
    }

    [Fact]
    public void Messages_FollowTheMessageStyle()
    {
        foreach (var error in new[] { BalanceErrors.InvalidAsOf, BalanceErrors.AsOfInTheFuture })
        {
            MessageStyle.Violations(error.Message).ShouldBeEmpty(error.Message);
        }
    }
}
