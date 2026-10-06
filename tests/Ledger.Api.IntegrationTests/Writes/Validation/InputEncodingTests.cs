using System.Text;
using Ledger.Api.Validation;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class InputEncodingTests
{
    private const string Description = "depósito em conta";

    private readonly RegisterEntryRequestReader _entries = new(
        new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 14, 3, 11, TimeSpan.Zero)),
        Options.Create(new LedgerOptions()));

    private ReadResult<RegisterEntryInput> ReadEntry(byte[] body) => _entries.Read(body);

    private static byte[] EntryWith(string descriptionToken) =>
        Encoding.UTF8.GetBytes(
            $"{{\"type\":\"CREDIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"description\":{descriptionToken}}}");

    [Fact]
    public void Description_WithAccents_IsReadFromUtf8BytesWithoutLosingAnyLetter()
    {
        var result = ReadEntry(EntryWith("\"depósito em conta\""));

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBe(Description);
    }

    [Fact]
    public void Description_WrittenWithAnEscapeSequence_IsTheSameTextAsTheRawLetter()
    {
        var escaped = ReadEntry(EntryWith("\"dep\\u00f3sito em conta\""));
        var raw = ReadEntry(EntryWith("\"depósito em conta\""));

        escaped.IsValid.ShouldBeTrue();
        escaped.Value.Description.ShouldBe(raw.Value.Description);
    }

    [Theory]
    [InlineData("conciliação de cobrança")]
    [InlineData("estorno de crédito indevido")]
    [InlineData("pagamento à vista")]
    [InlineData("ÁÉÍÓÚ ÀÂÊÔ ÃÕ Ç Ü")]
    public void Description_WithPortugueseLetters_RoundTripsThroughTheReader(string text)
    {
        var result = ReadEntry(EntryWith($"\"{text}\""));

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBe(text);
    }

    [Fact]
    public void Description_OfTheReversal_KeepsTheAccents()
    {
        var result = ReverseEntryRequestReader.Read(
            Encoding.UTF8.GetBytes("{\"description\":\"estorno de depósito em conta\"}"));

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBe("estorno de depósito em conta");
    }

    [Fact]
    public void Description_WithAccents_CountsLettersAndNotBytes()
    {
        var text = string.Concat(Enumerable.Repeat("ç", EntryRequestLimits.MaxDescriptionLength));

        ReadEntry(EntryWith($"\"{text}\"")).IsValid.ShouldBeTrue();
        ReadEntry(EntryWith($"\"{text}ç\"")).Issues.ShouldHaveSingleItem().Reason.ShouldBe("TOO_LONG");
    }

    [Fact]
    public void Body_WithABytePairThatIsNotUtf8_NeverThrowsAndIsRefused()
    {
        byte[] body =
        [
            .. Encoding.UTF8.GetBytes("{\"type\":\"CREDIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"description\":\"dep"),
            0xF3,
            .. Encoding.UTF8.GetBytes("sito\"}")
        ];

        var result = ReadEntry(body);

        result.IsValid.ShouldBeFalse();

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("description");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'description' deve estar em UTF-8 válido.");
    }

    [Fact]
    public void Body_WithAPropertyNameThatIsNotUtf8_NeverThrowsAndIsOneInvalidJsonIssueOnTheRoot()
    {
        byte[] body =
        [
            .. Encoding.UTF8.GetBytes("{\"type\":\"CREDIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"descri"),
            0xE7,
            0xE3,
            .. Encoding.UTF8.GetBytes("o\":\"Pix enviado\"}")
        ];

        var issue = ReadEntry(body).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("$");
        issue.Reason.ShouldBe("INVALID_JSON");
        issue.Message.ShouldBe("O corpo da requisição deve ser um único objeto JSON.");
        ReverseEntryRequestReader.Read(body).Issues.ShouldHaveSingleItem().Reason.ShouldBe("INVALID_JSON");
        CreateAccountRequestReader.Read(body).Issues.ShouldHaveSingleItem().Reason.ShouldBe("INVALID_JSON");
    }
}
