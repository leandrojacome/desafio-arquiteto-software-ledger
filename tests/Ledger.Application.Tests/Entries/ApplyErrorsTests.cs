using Ledger.Application.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class ApplyErrorsTests
{
    [Fact]
    public void NotMatched_IsAnInternalSignalWithItsOwnCode()
    {
        ApplyErrors.NotMatched.Code.ShouldBe("ENTRY_NOT_APPLIED");
        ApplyErrors.NotMatched.Kind.ShouldBe(ErrorKind.Unprocessable);
    }

    [Fact]
    public void NotMatched_HasTheMessageInPortuguese()
    {
        ApplyErrors.NotMatched.Message.ShouldBe(
            "A instrução condicional não encontrou nenhuma linha de conta correspondente.");
    }

    [Fact]
    public void NotMatched_NeverCarriesFormattingKeysQuotedValuesOrNumbers()
    {
        var message = ApplyErrors.NotMatched.Message;

        message.ShouldNotContain("{");
        message.ShouldNotContain("}");
        message.ShouldNotContain("\"");
        message.ShouldEndWith(".");
        message.Any(char.IsAsciiDigit).ShouldBeFalse();
    }
}
