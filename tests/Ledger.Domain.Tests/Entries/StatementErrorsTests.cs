using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Domain.Tests.Support;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class StatementErrorsTests
{
    [Fact]
    public void InvalidCursor_IsAValidationFailure()
    {
        StatementErrors.InvalidCursor.Code.ShouldBe("VALIDATION_FAILED");
        StatementErrors.InvalidCursor.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void InvalidCursor_HasTheMessageOfTheReadContract()
    {
        StatementErrors.InvalidCursor.Message.ShouldBe("O cursor não é válido para esta conta.");
    }

    [Fact]
    public void InvalidCursor_DoesNotSayWhichVerificationFailed()
    {
        var message = StatementErrors.InvalidCursor.Message;

        message.ShouldNotContain("assinatura", Case.Insensitive);
        message.ShouldNotContain("expirad", Case.Insensitive);
        message.ShouldNotContain("tamanho", Case.Insensitive);
        message.ShouldNotContain("{");
    }

    [Fact]
    public void InvalidCursor_FollowsTheMessageStyle()
    {
        MessageStyle.Violations(StatementErrors.InvalidCursor.Message).ShouldBeEmpty(StatementErrors.InvalidCursor.Message);
    }
}
