using System.Globalization;
using Ledger.Application.Entries;
using Ledger.Application.Tests.Entries.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;

namespace Ledger.Application.Tests.Entries;

[Trait("Category", "Unit")]
public sealed class CanonicalRequestHashTests
{
    private static string Hex(byte[] hash) => Convert.ToHexStringLower(hash);

    private static RegisterEntryCommand Registration(
        EntryType type = EntryType.Debit,
        string amount = "80.00",
        string currency = "BRL",
        DateTimeOffset? occurredAt = null,
        string? description = EntryFixtures.Description,
        string? reference = EntryFixtures.Reference,
        AccountId? account = null,
        IdempotencyKey? key = null,
        string clientId = EntryFixtures.ClientId)
    {
        var money = Money.Create(decimal.Parse(amount, CultureInfo.InvariantCulture), currency).Value;

        return new RegisterEntryCommand(
            account ?? EntryFixtures.Account,
            key ?? EntryFixtures.Key,
            type,
            money,
            occurredAt,
            description,
            reference,
            clientId,
            EntryFixtures.CorrelationId,
            null);
    }

    private static ReverseEntryCommand Reversal(string? description) =>
        EntryFixtures.ReverseCommand(description: description) with { OriginalEntryId = EntryFixtures.Entry };

    [Fact]
    public void CurrentVersion_IsOne()
    {
        CanonicalRequestHash.CurrentVersion.ShouldBe(1);
    }

    [Fact]
    public void ForRegistration_CompleteDebit_MatchesVectorOne()
    {
        var hash = CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.OccurredAt));

        Hex(hash).ShouldBe("5fe1f2884e903b6e85b22f47e9b1feae60fac2ffeeecdf9f0e027b57a07da8be");
    }

    [Fact]
    public void ForRegistration_MinimalCredit_MatchesVectorTwo()
    {
        var hash = CanonicalRequestHash.ForRegistration(
            Registration(EntryType.Credit, "25.50", description: null, reference: null));

        Hex(hash).ShouldBe("c7ecef52b971b3d01150245f68f6131af4bc2e64decd779e01b9700c5e55a985");
    }

    [Fact]
    public void ForReversal_WithDescription_MatchesVectorThree()
    {
        var hash = CanonicalRequestHash.ForReversal(Reversal("Duplicate charge confirmed by reconciliation"));

        Hex(hash).ShouldBe("62081fa4748137abd0a5e40df05c7fffea0f3bba8699e286ffd78402404aaedd");
    }

    [Fact]
    public void ForReversal_WithoutDescription_MatchesVectorFour()
    {
        var hash = CanonicalRequestHash.ForReversal(Reversal(null));

        Hex(hash).ShouldBe("2126479df4ba5b4bf4a7260b4eccfd294db6a735c66e7054ccc2958063c73be0");
    }

    [Fact]
    public void ForRegistration_CompleteDebitWithNinetyReais_MatchesVectorFive()
    {
        var hash = CanonicalRequestHash.ForRegistration(
            Registration(amount: "90.00", occurredAt: EntryFixtures.OccurredAt));

        Hex(hash).ShouldBe("60dd6c42b73f1908980ee76b99e857f8ee23bf5329be030b114ce9cfa9892167");
    }

    [Theory]
    [InlineData("80")]
    [InlineData("80.0")]
    [InlineData("80.00")]
    public void ForRegistration_AmountWrittenInDifferentWays_ProducesTheSameHash(string amount)
    {
        var reference = CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.OccurredAt));

        var hash = CanonicalRequestHash.ForRegistration(Registration(amount: amount, occurredAt: EntryFixtures.OccurredAt));

        hash.ShouldBe(reference);
    }

    [Fact]
    public void ForRegistration_OccurredAtWithOffsetAndInUtc_ProducesTheSameHash()
    {
        var local = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));

        var withOffset = CanonicalRequestHash.ForRegistration(Registration(occurredAt: local));
        var inUtc = CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.OccurredAt));

        withOffset.ShouldBe(inUtc);
    }

    [Theory]
    [InlineData(" Pix enviado ")]
    [InlineData("Pix enviado")]
    [InlineData("\tPix enviado\t")]
    public void ForRegistration_DescriptionWithSurroundingSpaces_ProducesTheSameHash(string description)
    {
        var reference = CanonicalRequestHash.ForRegistration(Registration(description: "Pix enviado"));

        var hash = CanonicalRequestHash.ForRegistration(Registration(description: description));

        hash.ShouldBe(reference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForRegistration_AbsentEmptyOrBlankDescription_ProducesTheSameHash(string? description)
    {
        var reference = CanonicalRequestHash.ForRegistration(Registration(description: null));

        var hash = CanonicalRequestHash.ForRegistration(Registration(description: description));

        hash.ShouldBe(reference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForReversal_AbsentEmptyOrBlankDescription_ProducesTheSameHash(string? description)
    {
        var reference = CanonicalRequestHash.ForReversal(Reversal(null));

        var hash = CanonicalRequestHash.ForReversal(Reversal(description));

        hash.ShouldBe(reference);
    }

    [Fact]
    public void ForRegistration_DifferentIdempotencyKeys_ProduceTheSameHash()
    {
        var first = CanonicalRequestHash.ForRegistration(Registration());
        var second = CanonicalRequestHash.ForRegistration(
            Registration(key: IdempotencyKey.From("another-key-0002").Value));

        second.ShouldBe(first);
    }

    [Fact]
    public void ForRegistration_EveryChangedField_ProducesADifferentHash()
    {
        var otherAccount = AccountId.From(Guid.Parse("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d34")).Value;
        var baseline = Hex(CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.OccurredAt)));

        var variants = new[]
        {
            Registration(EntryType.Credit, occurredAt: EntryFixtures.OccurredAt),
            Registration(amount: "80.01", occurredAt: EntryFixtures.OccurredAt),
            Registration(currency: "EUR", occurredAt: EntryFixtures.OccurredAt),
            Registration(occurredAt: EntryFixtures.OccurredAt.AddSeconds(1)),
            Registration(occurredAt: null),
            Registration(description: "Pix Enviado", occurredAt: EntryFixtures.OccurredAt),
            Registration(description: null, occurredAt: EntryFixtures.OccurredAt),
            Registration(reference: "another-reference", occurredAt: EntryFixtures.OccurredAt),
            Registration(reference: null, occurredAt: EntryFixtures.OccurredAt),
            Registration(account: otherAccount, occurredAt: EntryFixtures.OccurredAt),
            Registration(occurredAt: EntryFixtures.OccurredAt, clientId: "cards-core")
        };

        var hashes = variants.Select(variant => Hex(CanonicalRequestHash.ForRegistration(variant))).ToList();

        hashes.ShouldAllBe(hash => hash != baseline);
        hashes.Distinct().Count().ShouldBe(hashes.Count);
    }

    [Fact]
    public void ForRegistration_PresentOccurredAt_DiffersFromTheAbsentOne()
    {
        var absent = CanonicalRequestHash.ForRegistration(Registration(occurredAt: null));
        var present = CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.RecordedAt));

        present.ShouldNotBe(absent);
    }

    [Fact]
    public void ForRegistration_AnotherClientWithTheSameBody_ProducesADifferentHash()
    {
        var first = CanonicalRequestHash.ForRegistration(Registration(occurredAt: EntryFixtures.OccurredAt));

        var second = CanonicalRequestHash.ForRegistration(
            Registration(occurredAt: EntryFixtures.OccurredAt, clientId: "cards-core"));

        second.ShouldNotBe(first);
    }

    [Fact]
    public void ForReversal_AnotherClientWithTheSameBody_ProducesADifferentHash()
    {
        var first = CanonicalRequestHash.ForReversal(EntryFixtures.ReverseCommand());

        var second = CanonicalRequestHash.ForReversal(EntryFixtures.ReverseCommand() with { ClientId = "cards-core" });

        second.ShouldNotBe(first);
    }

    [Fact]
    public void ForRegistration_SameClientRepeatingTheBody_ProducesTheSameHash()
    {
        var first = CanonicalRequestHash.ForRegistration(Registration(clientId: "cards-core"));
        var second = CanonicalRequestHash.ForRegistration(Registration(clientId: "cards-core"));

        second.ShouldBe(first);
    }

    [Fact]
    public void ForReversal_DifferentOriginalEntry_ProducesADifferentHash()
    {
        var first = CanonicalRequestHash.ForReversal(EntryFixtures.ReverseCommand());
        var second = CanonicalRequestHash.ForReversal(
            EntryFixtures.ReverseCommand() with { OriginalEntryId = EntryFixtures.Entry });

        second.ShouldNotBe(first);
    }

    [Fact]
    public void ForRegistration_AndForReversal_NeverCollide()
    {
        var registration = CanonicalRequestHash.ForRegistration(
            Registration(EntryType.Credit, description: null, reference: null));
        var reversal = CanonicalRequestHash.ForReversal(Reversal(null));

        reversal.ShouldNotBe(registration);
    }

    [Fact]
    public void Matches_EqualHashes_ReturnsTrue()
    {
        var first = CanonicalRequestHash.ForRegistration(Registration());
        var second = CanonicalRequestHash.ForRegistration(Registration());

        CanonicalRequestHash.Matches(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Matches_DifferentHashes_ReturnsFalse()
    {
        var first = CanonicalRequestHash.ForRegistration(Registration());
        var second = CanonicalRequestHash.ForRegistration(Registration(amount: "90.00"));

        CanonicalRequestHash.Matches(first, second).ShouldBeFalse();
    }

    [Fact]
    public void Matches_HashesOfDifferentLength_ReturnsFalse()
    {
        var hash = CanonicalRequestHash.ForRegistration(Registration());

        CanonicalRequestHash.Matches(hash, hash.AsSpan(0, 16)).ShouldBeFalse();
    }
}
