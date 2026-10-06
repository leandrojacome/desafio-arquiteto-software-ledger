using System.Reflection;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Domain.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class EntryTests
{
    private const int MaxDescriptionLength = 140;
    private const int MaxReferenceLength = 100;

    private static readonly EntryId Id = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")).Value;
    private static readonly EntryId OriginalId = EntryId.From(Guid.Parse("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f11")).Value;
    private static readonly AccountId Account = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")).Value;
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 1, 14, 3, 10, TimeSpan.Zero);

    [Fact]
    public void Credit_WithPositiveMoney_KeepsEveryField()
    {
        var amount = Brl(80.00m);

        var result = Entry.Credit(Id, Account, amount, OccurredAt, "Pix recebido", "E18236120202610011403s0a1b2c3d4e");

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value;
        entry.Id.ShouldBe(Id);
        entry.AccountId.ShouldBe(Account);
        entry.Type.ShouldBe(EntryType.Credit);
        entry.Amount.ShouldBe(amount);
        entry.OccurredAt.ShouldBe(OccurredAt);
        entry.Description.ShouldBe("Pix recebido");
        entry.Reference.ShouldBe("E18236120202610011403s0a1b2c3d4e");
        entry.ReversesEntryId.ShouldBeNull();
        entry.IsReversal.ShouldBeFalse();
        entry.SignedDelta.ShouldBe(80.00m);
    }

    [Fact]
    public void Debit_WithPositiveMoney_KeepsEveryField()
    {
        var amount = Brl(80.00m);

        var result = Entry.Debit(Id, Account, amount, OccurredAt, "Pix enviado", "ref-0001");

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value;
        entry.Id.ShouldBe(Id);
        entry.AccountId.ShouldBe(Account);
        entry.Type.ShouldBe(EntryType.Debit);
        entry.Amount.ShouldBe(amount);
        entry.OccurredAt.ShouldBe(OccurredAt);
        entry.Description.ShouldBe("Pix enviado");
        entry.Reference.ShouldBe("ref-0001");
        entry.ReversesEntryId.ShouldBeNull();
        entry.IsReversal.ShouldBeFalse();
        entry.SignedDelta.ShouldBe(-80.00m);
    }

    [Fact]
    public void Credit_WithoutOptionalFields_KeepsThemAbsent()
    {
        var entry = Entry.Credit(Id, Account, Brl(10m), null, null, null).Value;

        entry.OccurredAt.ShouldBeNull();
        entry.Description.ShouldBeNull();
        entry.Reference.ShouldBeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-0.01")]
    [InlineData("-80")]
    public void CreditAndDebit_WithZeroOrNegativeMoney_ReturnMustBePositive(string text)
    {
        var amount = Money.Create(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), "BRL").Value;

        Entry.Credit(Id, Account, amount, null, null, null).Error.ShouldBe(MoneyErrors.MustBePositive);
        Entry.Debit(Id, Account, amount, null, null, null).Error.ShouldBe(MoneyErrors.MustBePositive);
    }

    [Fact]
    public void CreditAndDebit_WithTheSmallestAndTheLargestAmount_AreAccepted()
    {
        var smallest = Brl(0.01m);
        var largest = Brl(Money.MaxAbsoluteAmount);

        Entry.Credit(Id, Account, smallest, null, null, null).IsSuccess.ShouldBeTrue();
        Entry.Debit(Id, Account, largest, null, null, null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Credit_WithAnOccurredAtInAnotherOffset_KeepsTheInstantInUtc()
    {
        var local = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));

        var entry = Entry.Credit(Id, Account, Brl(10m), local, null, null).Value;

        entry.OccurredAt.ShouldBe(OccurredAt);
        entry.OccurredAt.GetValueOrDefault().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Credit_WithAnOccurredAtInTheDistantPast_IsAccepted()
    {
        var past = new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var result = Entry.Credit(Id, Account, Brl(10m), past, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccurredAt.ShouldBe(past);
    }

    [Fact]
    public void Credit_WithAnOccurredAtKeepingMicrosecondPrecision_KeepsEveryTick()
    {
        var precise = OccurredAt.AddTicks(1_234_560);

        var entry = Entry.Credit(Id, Account, Brl(10m), precise, null, null).Value;

        entry.OccurredAt.ShouldBe(precise);
    }

    [Theory]
    [InlineData(" Pix enviado ", "Pix enviado")]
    [InlineData("Pix enviado", "Pix enviado")]
    [InlineData("\tCobrança duplicada\n", "Cobrança duplicada")]
    [InlineData("a", "a")]
    [InlineData("com  espaços  internos", "com  espaços  internos")]
    public void Credit_WithDescription_KeepsItTrimmed(string description, string expected)
    {
        var entry = Entry.Credit(Id, Account, Brl(10m), null, description, null).Value;

        entry.Description.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    [InlineData("\t \n")]
    public void Credit_WithEmptyOrBlankDescription_TreatsItAsAbsent(string description)
    {
        var entry = Entry.Credit(Id, Account, Brl(10m), null, description, null).Value;

        entry.Description.ShouldBeNull();
    }

    [Fact]
    public void Credit_WithDescriptionOfExactlyTheMaximumLength_IsAccepted()
    {
        var description = new string('d', MaxDescriptionLength);

        var result = Entry.Credit(Id, Account, Brl(10m), null, description, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.ShouldBe(description);
    }

    [Fact]
    public void Credit_WithDescriptionOneCharacterAboveTheMaximumLength_ReturnsInvalidDescription()
    {
        var description = new string('d', MaxDescriptionLength + 1);

        var result = Entry.Credit(Id, Account, Brl(10m), null, description, null);

        result.Error.ShouldBe(EntryErrors.InvalidDescription);
    }

    [Fact]
    public void Credit_WithDescriptionOfMaximumLengthPaddedWithSpaces_IsAcceptedAfterTrimming()
    {
        var description = "  " + new string('d', MaxDescriptionLength) + "  ";

        var result = Entry.Credit(Id, Account, Brl(10m), null, description, null);

        result.Value.Description.ShouldBe(new string('d', MaxDescriptionLength));
    }

    [Fact]
    public void Credit_WithMaximumLengthOfCharactersOutsideTheBasicPlane_CountsCharactersAndNotCodeUnits()
    {
        var description = string.Concat(Enumerable.Repeat("\U0001F600", MaxDescriptionLength));

        var accepted = Entry.Credit(Id, Account, Brl(10m), null, description, null);
        var refused = Entry.Credit(Id, Account, Brl(10m), null, description + "\U0001F600", null);

        accepted.IsSuccess.ShouldBeTrue();
        refused.Error.ShouldBe(EntryErrors.InvalidDescription);
    }

    [Theory]
    [InlineData("duas\nlinhas")]
    [InlineData("aba\taqui")]
    [InlineData("retorno\rde carro")]
    [InlineData("nulo\u0000no meio")]
    [InlineData("del\u007fno meio")]
    [InlineData("controle\u0085c1")]
    [InlineData("escape\u001bsequencia")]
    public void Credit_WithControlCharacterInTheDescription_ReturnsInvalidDescription(string description)
    {
        var result = Entry.Credit(Id, Account, Brl(10m), null, description, null);

        result.Error.ShouldBe(EntryErrors.InvalidDescription);
    }

    [Theory]
    [InlineData("Cobrança duplicada confirmada pela conciliação")]
    [InlineData("日本語の説明")]
    [InlineData("Pix \U0001F4B8 enviado")]
    public void Credit_WithNonAsciiPrintableDescription_IsAccepted(string description)
    {
        var result = Entry.Credit(Id, Account, Brl(10m), null, description, null);

        result.Value.Description.ShouldBe(description);
    }

    [Theory]
    [InlineData("E18236120202610011403s0a1b2c3d4e")]
    [InlineData("a")]
    [InlineData("!~")]
    [InlineData("0192b7c4-5d11-7a3e-9c2f-3b1e7d9a4f10")]
    public void Debit_WithVisibleAsciiReference_KeepsIt(string reference)
    {
        var entry = Entry.Debit(Id, Account, Brl(10m), null, null, reference).Value;

        entry.Reference.ShouldBe(reference);
    }

    [Fact]
    public void Debit_WithEmptyReference_TreatsItAsAbsent()
    {
        var entry = Entry.Debit(Id, Account, Brl(10m), null, null, string.Empty).Value;

        entry.Reference.ShouldBeNull();
    }

    [Fact]
    public void Debit_WithReferenceOfExactlyTheMaximumLength_IsAccepted()
    {
        var reference = new string('r', MaxReferenceLength);

        var result = Entry.Debit(Id, Account, Brl(10m), null, null, reference);

        result.Value.Reference.ShouldBe(reference);
    }

    [Fact]
    public void Debit_WithReferenceOneCharacterAboveTheMaximumLength_ReturnsInvalidReference()
    {
        var result = Entry.Debit(Id, Account, Brl(10m), null, null, new string('r', MaxReferenceLength + 1));

        result.Error.ShouldBe(EntryErrors.InvalidReference);
    }

    [Theory]
    [InlineData("com espaco")]
    [InlineData(" ")]
    [InlineData(" inicio")]
    [InlineData("fim ")]
    [InlineData("tab\taqui")]
    [InlineData("referência")]
    [InlineData("ç")]
    [InlineData("del\u007f")]
    [InlineData("emoji\U0001F600")]
    public void Debit_WithReferenceOutsideVisibleAscii_ReturnsInvalidReference(string reference)
    {
        var result = Entry.Debit(Id, Account, Brl(10m), null, null, reference);

        result.Error.ShouldBe(EntryErrors.InvalidReference);
    }

    [Fact]
    public void Credit_WithSeveralProblems_ReportsTheAmountFirstThenTheDescriptionThenTheReference()
    {
        var zero = Brl(0m);
        var longDescription = new string('d', MaxDescriptionLength + 1);

        Entry.Credit(Id, Account, zero, null, longDescription, "bad ref").Error.ShouldBe(MoneyErrors.MustBePositive);
        Entry.Credit(Id, Account, Brl(1m), null, longDescription, "bad ref").Error.ShouldBe(EntryErrors.InvalidDescription);
        Entry.Credit(Id, Account, Brl(1m), null, "ok", "bad ref").Error.ShouldBe(EntryErrors.InvalidReference);
    }

    [Fact]
    public void ReversalOf_ForADebit_IsACreditOfTheSameAmountPointingToTheOriginal()
    {
        var plan = PlanFor(EntryType.Debit, Brl(80.00m));

        var result = Entry.ReversalOf(plan, Id, Account, "x");

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value;
        entry.Id.ShouldBe(Id);
        entry.AccountId.ShouldBe(Account);
        entry.Type.ShouldBe(EntryType.Credit);
        entry.Amount.ShouldBe(Brl(80.00m));
        entry.ReversesEntryId.ShouldBe(OriginalId);
        entry.IsReversal.ShouldBeTrue();
        entry.OccurredAt.ShouldBeNull();
        entry.Reference.ShouldBeNull();
        entry.Description.ShouldBe("x");
        entry.SignedDelta.ShouldBe(80.00m);
    }

    [Fact]
    public void ReversalOf_ForACredit_IsADebitOfTheSameAmount()
    {
        var plan = PlanFor(EntryType.Credit, Brl(1234.56m));

        var entry = Entry.ReversalOf(plan, Id, Account, null).Value;

        entry.Type.ShouldBe(EntryType.Debit);
        entry.Amount.Amount.ShouldBe(1234.56m);
        entry.Amount.Currency.ShouldBe("BRL");
        entry.Description.ShouldBeNull();
        entry.SignedDelta.ShouldBe(-1234.56m);
    }

    [Fact]
    public void ReversalOf_KeepsTheCurrencyOfTheOriginal()
    {
        var plan = PlanFor(EntryType.Debit, Money.Create(10m, "BRL").Value);

        var entry = Entry.ReversalOf(plan, Id, Account, null).Value;

        entry.Amount.Currency.ShouldBe("BRL");
    }

    [Fact]
    public void ReversalOf_WithDescriptionAboveTheLimit_ReturnsInvalidDescription()
    {
        var plan = PlanFor(EntryType.Debit, Brl(10m));

        var result = Entry.ReversalOf(plan, Id, Account, new string('d', MaxDescriptionLength + 1));

        result.Error.ShouldBe(EntryErrors.InvalidDescription);
    }

    [Fact]
    public void ReversalOf_WithBlankDescription_TreatsItAsAbsent()
    {
        var plan = PlanFor(EntryType.Debit, Brl(10m));

        var entry = Entry.ReversalOf(plan, Id, Account, "   ").Value;

        entry.Description.ShouldBeNull();
    }

    [Fact]
    public void Entry_IsSealed()
    {
        typeof(Entry).IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void Entry_HasNoPublicConstructor()
    {
        typeof(Entry).GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
    }

    [Fact]
    public void Entry_HasNoPublicSetter()
    {
        var publicSetters = typeof(Entry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToList();

        publicSetters.ShouldBeEmpty();
    }

    [Fact]
    public void Entry_HasNoPublicField()
    {
        typeof(Entry).GetFields(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
    }

    private static Money Brl(decimal amount) => Money.Create(amount, "BRL").Value;

    private static ReversalPlan PlanFor(EntryType originalType, Money amount)
    {
        var candidate = new ReversalCandidate(OriginalId, originalType, amount, null, null);

        return candidate.Plan().Value;
    }
}
