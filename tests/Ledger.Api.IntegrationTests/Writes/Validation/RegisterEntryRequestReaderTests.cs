using System.Text;
using Ledger.Api.Validation;
using Ledger.Domain.Entries;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class RegisterEntryRequestReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 14, 3, 11, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);

    private RegisterEntryRequestReader Reader(int toleranceMinutes = 5) =>
        new(_time, Options.Create(new LedgerOptions { OccurredAtFutureToleranceMinutes = toleranceMinutes }));

    private ReadResult<RegisterEntryInput> Read(string json) => Reader().Read(Encoding.UTF8.GetBytes(json));

    private static string WithAmount(string amountToken) =>
        $"{{\"type\":\"DEBIT\",\"amount\":{amountToken},\"currency\":\"BRL\"}}";

    private static string WithField(string field, string token) =>
        $"{{\"type\":\"DEBIT\",\"amount\":\"10.00\",\"currency\":\"BRL\",\"{field}\":{token}}}";

    private static (string Field, string Reason) Single(ReadResult<RegisterEntryInput> result)
    {
        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();

        return (issue.Field, issue.Reason);
    }

    [Fact]
    public void ExampleOfTheContract_IsReadWithEveryField()
    {
        var result = Read(
            "{\"type\":\"DEBIT\",\"amount\":\"80.00\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01T11:03:10-03:00\"," +
            "\"description\":\"Pix enviado\",\"reference\":\"E18236120202610011403s0a1b2c3d4e\"}");

        result.IsValid.ShouldBeTrue();
        result.Value.Type.ShouldBe(EntryType.Debit);
        result.Value.Amount.Amount.ShouldBe(80.00m);
        result.Value.Amount.Currency.ShouldBe("BRL");
        result.Value.OccurredAt.ShouldBe(new DateTimeOffset(2026, 10, 1, 14, 3, 10, TimeSpan.Zero));
        result.Value.Description.ShouldBe("Pix enviado");
        result.Value.Reference.ShouldBe("E18236120202610011403s0a1b2c3d4e");
    }

    [Theory]
    [InlineData("\"80\"", "80.00")]
    [InlineData("\"80.5\"", "80.50")]
    [InlineData("\"0.01\"", "0.01")]
    [InlineData("\"999999999.99\"", "999999999.99")]
    public void Amount_WithAcceptedText_IsReadWithTwoDecimalPlaces(string token, string expected)
    {
        var result = Read(WithAmount(token));

        result.IsValid.ShouldBeTrue();
        result.Value.Amount.ToDecimalString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("80", "INVALID_FORMAT")]
    [InlineData("\"abc\"", "INVALID_FORMAT")]
    [InlineData("\"1e3\"", "INVALID_FORMAT")]
    [InlineData("\"80,00\"", "INVALID_FORMAT")]
    [InlineData("\".5\"", "INVALID_FORMAT")]
    [InlineData("\"80.\"", "INVALID_FORMAT")]
    [InlineData("\"\"", "INVALID_FORMAT")]
    [InlineData("\" 10.00\"", "INVALID_FORMAT")]
    [InlineData("\"-10.00\"", "OUT_OF_RANGE")]
    [InlineData("\"0.00\"", "OUT_OF_RANGE")]
    [InlineData("\"0\"", "OUT_OF_RANGE")]
    [InlineData("\"1000000000.00\"", "OUT_OF_RANGE")]
    [InlineData("\"99999999999999999999999999999999.00\"", "OUT_OF_RANGE")]
    [InlineData("\"10.001\"", "TOO_MANY_DECIMALS")]
    [InlineData("\"-10.001\"", "OUT_OF_RANGE")]
    public void Amount_WithRejectedValue_ReportsTheReasonOfTheFirstRuleThatFails(string token, string reason)
    {
        Single(Read(WithAmount(token))).ShouldBe(("amount", reason));
    }

    [Theory]
    [InlineData("\"10,50\"")]
    [InlineData("\"1.000,00\"")]
    [InlineData("\"R$ 10.00\"")]
    [InlineData("\"R$10.00\"")]
    [InlineData("\"1,000.00\"")]
    public void Amount_WithACurrencySymbolOrASeparatorOfThousands_ExplainsTheDecimalTextOfTheContract(string token)
    {
        var issue = Read(WithAmount(token)).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("amount");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            "O campo 'amount' deve ser um texto decimal com ponto, como '80.00', sem símbolo de moeda nem separador de milhar.");
    }

    [Fact]
    public void Type_WhenAbsent_IsRequired()
    {
        Single(Read("{\"amount\":\"10.00\",\"currency\":\"BRL\"}")).ShouldBe(("type", "REQUIRED"));
    }

    [Theory]
    [InlineData("\"credit\"", "NOT_ALLOWED")]
    [InlineData("\"TRANSFER\"", "NOT_ALLOWED")]
    [InlineData("\"\"", "NOT_ALLOWED")]
    [InlineData("1", "INVALID_FORMAT")]
    [InlineData("true", "INVALID_FORMAT")]
    [InlineData("null", "REQUIRED")]
    public void Type_WithAnythingButTheExactTexts_IsRejected(string token, string reason)
    {
        var result = Read($"{{\"type\":{token},\"amount\":\"10.00\",\"currency\":\"BRL\"}}");

        Single(result).ShouldBe(("type", reason));
    }

    [Theory]
    [InlineData("\"CREDIT\"", EntryType.Credit)]
    [InlineData("\"DEBIT\"", EntryType.Debit)]
    public void Type_WithTheExactTexts_IsAccepted(string token, EntryType expected)
    {
        var result = Read($"{{\"type\":{token},\"amount\":\"10.00\",\"currency\":\"BRL\"}}");

        result.IsValid.ShouldBeTrue();
        result.Value.Type.ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"brl\"")]
    [InlineData("\"BR\"")]
    [InlineData("\"BRLL\"")]
    [InlineData("\"B1L\"")]
    [InlineData("12")]
    public void Currency_WhenNotThreeUppercaseLetters_IsAnInvalidFormat(string token)
    {
        var result = Read($"{{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":{token}}}");

        Single(result).ShouldBe(("currency", "INVALID_FORMAT"));
    }

    [Fact]
    public void Currency_WhenAbsent_IsRequired()
    {
        Single(Read("{\"type\":\"CREDIT\",\"amount\":\"10.00\"}")).ShouldBe(("currency", "REQUIRED"));
    }

    [Fact]
    public void Currency_OtherThanTheAccountOne_IsNotTheBusinessOfTheReader()
    {
        var result = Read("{\"type\":\"CREDIT\",\"amount\":\"10.00\",\"currency\":\"EUR\"}");

        result.IsValid.ShouldBeTrue();
        result.Value.Amount.Currency.ShouldBe("EUR");
    }

    [Theory]
    [InlineData("2026-10-01T14:08:11Z")]
    [InlineData("2026-10-01T11:08:11-03:00")]
    [InlineData("2026-10-01T14:08:10.999999Z")]
    [InlineData("2001-01-01T00:00:00Z")]
    [InlineData("2026-10-01T14:03:10.123456+00:00")]
    public void OccurredAt_UpToFiveMinutesAheadAndAnyTimeBefore_IsAccepted(string text)
    {
        var result = Read(WithField("occurredAt", $"\"{text}\""));

        result.IsValid.ShouldBeTrue();
        result.Value.OccurredAt.ShouldNotBeNull().Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("2026-10-01T14:03:10")]
    [InlineData("2026-10-01T14:03:10.5")]
    [InlineData("2026-10-01T14:03:10.123456")]
    public void OccurredAt_WithoutTimeZone_IsMissingTimeZoneWithTheGuidanceToInformIt(string text)
    {
        var result = Read(WithField("occurredAt", $"\"{text}\""));

        var issue = result.Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("occurredAt");
        issue.Reason.ShouldBe("MISSING_TIME_ZONE");
        issue.Message.ShouldBe("Informe o fuso horário no campo 'occurredAt', por exemplo 'Z' ou '-03:00'.");
    }

    [Theory]
    [InlineData("2026-10-01T14:08:11.000001Z")]
    [InlineData("2026-10-01T11:08:12-03:00")]
    [InlineData("2030-01-01T00:00:00Z")]
    public void OccurredAt_BeyondFiveMinutesAhead_IsInTheFuture(string text)
    {
        Single(Read(WithField("occurredAt", $"\"{text}\""))).ShouldBe(("occurredAt", "IN_THE_FUTURE"));
    }

    [Theory]
    [InlineData("2026-10-01 14:03:10Z")]
    [InlineData("2026-10-01T14:03:10.1234567Z")]
    [InlineData("2026-10-01T14:03:10+0000")]
    [InlineData("2026-10-01T14:03:10+25:00")]
    [InlineData("2026-10-01T14:03:10-00:00")]
    [InlineData("2026-10-01T14:03Z")]
    [InlineData("2026-10-01")]
    [InlineData("2026-13-01T00:00:00Z")]
    [InlineData("2026-10-01T24:00:00Z")]
    [InlineData("2026-10-01t14:03:10z")]
    [InlineData("")]
    public void OccurredAt_WithAnotherShape_IsAnInvalidFormat(string text)
    {
        Single(Read(WithField("occurredAt", $"\"{text}\""))).ShouldBe(("occurredAt", "INVALID_FORMAT"));
    }

    [Fact]
    public void OccurredAt_AsANumber_IsAnInvalidFormat()
    {
        Single(Read(WithField("occurredAt", "1759327390"))).ShouldBe(("occurredAt", "INVALID_FORMAT"));
    }

    [Fact]
    public void OccurredAt_WithAnotherTolerance_FollowsTheConfiguration()
    {
        var body = Encoding.UTF8.GetBytes(WithField("occurredAt", "\"2026-10-01T14:08:11Z\""));

        Reader(10).Read(body).IsValid.ShouldBeTrue();
        Reader(1).Read(body).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Description_WithOneHundredFortyCharactersAfterTrimming_IsAcceptedAndTrimmed()
    {
        var text = new string('a', 140);

        var result = Read(WithField("description", $"\"  {text} \""));

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBe(text);
    }

    [Fact]
    public void Description_WithOneHundredFortyOneCharacters_IsTooLong()
    {
        Single(Read(WithField("description", $"\"{new string('a', 141)}\""))).ShouldBe(("description", "TOO_LONG"));
    }

    [Fact]
    public void Description_CountsCharactersAndNotUtf16Units()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 140));

        Read(WithField("description", $"\"{text}\"")).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("a\\u0000b")]
    [InlineData("a\\nb")]
    [InlineData("a\\tb")]
    [InlineData("a\\u001fb")]
    public void Description_WithAControlCharacter_IsAnInvalidFormat(string escaped)
    {
        Single(Read(WithField("description", $"\"{escaped}\""))).ShouldBe(("description", "INVALID_FORMAT"));
    }

    [Theory]
    [InlineData("a\\nb")]
    [InlineData("a\\tb")]
    [InlineData("a\\r\\nb")]
    public void Description_WithALineBreakOrATab_ExplainsThatControlCharactersAreNotAllowed(string escaped)
    {
        var issue = Read(WithField("description", $"\"{escaped}\"")).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("description");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            "O campo 'description' não pode conter caracteres de controle, como quebra de linha ou tabulação.");
    }

    [Fact]
    public void Description_WithBytesThatAreNotUtf8_ExplainsThatTheTextMustBeValidUtf8()
    {
        byte[] body = [.. "{\"type\":\"DEBIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"description\":\"dep"u8, 0xF3, .. "sito\"}"u8];

        var issue = Reader().Read(body).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("description");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'description' deve estar em UTF-8 válido.");
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    public void Description_EmptyBlankOrNull_IsReadAsAbsent(string token)
    {
        var result = Read(WithField("description", token));

        result.IsValid.ShouldBeTrue();
        result.Value.Description.ShouldBeNull();
    }

    [Fact]
    public void Description_AsANumber_IsAnInvalidFormat()
    {
        Single(Read(WithField("description", "10"))).ShouldBe(("description", "INVALID_FORMAT"));
    }

    [Fact]
    public void Reference_WithOneHundredVisibleCharacters_IsAccepted()
    {
        var text = new string('R', 100);

        var result = Read(WithField("reference", $"\"{text}\""));

        result.IsValid.ShouldBeTrue();
        result.Value.Reference.ShouldBe(text);
    }

    [Fact]
    public void Reference_WithOneHundredOneCharacters_IsTooLong()
    {
        Single(Read(WithField("reference", $"\"{new string('R', 101)}\""))).ShouldBe(("reference", "TOO_LONG"));
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("a\\u00e7ucar")]
    [InlineData("tab\\there")]
    public void Reference_WithASpaceOrNonAsciiCharacter_IsAnInvalidFormat(string escaped)
    {
        Single(Read(WithField("reference", $"\"{escaped}\""))).ShouldBe(("reference", "INVALID_FORMAT"));
    }

    [Theory]
    [InlineData("dep\\u00f3sito")]
    [InlineData("com espa\\u00e7o")]
    [InlineData("com espaco")]
    public void Reference_WithAccentsOrSpaces_ExplainsThatOnlyVisibleAsciiIsAllowed(string escaped)
    {
        var issue = Read(WithField("reference", $"\"{escaped}\"")).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("reference");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe("O campo 'reference' deve ter apenas caracteres ASCII visíveis, sem espaços nem acentos.");
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("null")]
    public void Reference_EmptyOrNull_IsReadAsAbsent(string token)
    {
        var result = Read(WithField("reference", token));

        result.IsValid.ShouldBeTrue();
        result.Value.Reference.ShouldBeNull();
    }

    [Fact]
    public void OptionalFields_WhenAbsent_AreNull()
    {
        var result = Read(WithAmount("\"10.00\""));

        result.IsValid.ShouldBeTrue();
        result.Value.OccurredAt.ShouldBeNull();
        result.Value.Description.ShouldBeNull();
        result.Value.Reference.ShouldBeNull();
    }

    [Fact]
    public void RequiredFields_SetToNull_AreRequired()
    {
        var result = Read("{\"type\":null,\"amount\":null,\"currency\":null}");

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("type", "REQUIRED"), ("amount", "REQUIRED"), ("currency", "REQUIRED")]);
    }

    [Fact]
    public void UnknownProperty_IsReportedWithItsName()
    {
        var result = Read(WithField("holderName", "\"x\""));

        Single(result).ShouldBe(("holderName", "UNKNOWN_FIELD"));
    }

    [Fact]
    public void UnknownProperty_WithANameOutsideTheSafeAlphabet_IsReportedAsUnknown()
    {
        var result = Read(WithField("bad name!", "1"));

        Single(result).ShouldBe(("(unknown)", "UNKNOWN_FIELD"));
    }

    [Fact]
    public void UnknownProperty_WithATooLongName_IsReportedAsUnknown()
    {
        var result = Read(WithField(new string('x', 65), "1"));

        Single(result).ShouldBe(("(unknown)", "UNKNOWN_FIELD"));
    }

    [Fact]
    public void PropertyNames_AreCaseSensitive()
    {
        var result = Read("{\"Type\":\"DEBIT\",\"amount\":\"10.00\",\"currency\":\"BRL\"}");

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("type", "REQUIRED"), ("Type", "UNKNOWN_FIELD")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[{\"type\":\"DEBIT\"}]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("{ not json")]
    [InlineData("{\"type\":\"DEBIT\"} trailing")]
    [InlineData("{\"type\":\"DEBIT\",\"type\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\"}")]
    [InlineData("{\"type\":\"DEBIT\",\"\\u0074ype\":\"CREDIT\",\"amount\":\"1.00\",\"currency\":\"BRL\"}")]
    public void BodyThatIsNotASingleObject_IsOneInvalidJsonIssueOnTheRoot(string json)
    {
        Single(Read(json)).ShouldBe(("$", "INVALID_JSON"));
    }

    [Fact]
    public void InvalidUtf8_IsOneInvalidJsonIssueOrAnInvalidFormat()
    {
        byte[] body = [.. "{\"type\":\"DEBIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"description\":\"a"u8, 0xC3, 0x28, .. "\"}"u8];

        var result = Reader().Read(body);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem().Reason.ShouldBeOneOf("INVALID_JSON", "INVALID_FORMAT");
    }

    [Fact]
    public void SeveralWrongFields_AreAllReportedTogether()
    {
        var result = Read(
            "{\"type\":\"TRANSFER\",\"amount\":\"0.001\",\"currency\":\"BRL\",\"occurredAt\":\"2026-10-01T14:13:11Z\"}");

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
            [("type", "NOT_ALLOWED"), ("amount", "TOO_MANY_DECIMALS"), ("occurredAt", "IN_THE_FUTURE")]);
    }

    [Fact]
    public void Messages_NameTheFieldAndNeverRepeatTheReceivedValue()
    {
        const string canary = "canary-value-7f3a";
        var result = Read(
            $"{{\"type\":\"{canary}\",\"amount\":\"{canary}\",\"currency\":\"{canary}\",\"occurredAt\":\"{canary}\"," +
            $"\"description\":\"a\\u0001{canary}\",\"reference\":\"{canary} {canary}\",\"{canary}\":1}}");

        result.IsValid.ShouldBeFalse();
        result.Issues.Count.ShouldBeGreaterThanOrEqualTo(6);
        result.Issues.ShouldAllBe(issue => !issue.Message.Contains(canary, StringComparison.Ordinal));
        result.Issues.ShouldAllBe(issue => issue.Message.Length > 0);
    }

    [Fact]
    public void ValidInput_DoesNotPrintFreeTextInToString()
    {
        var result = Read(
            "{\"type\":\"DEBIT\",\"amount\":\"1.00\",\"currency\":\"BRL\",\"description\":\"free text\",\"reference\":\"ref-1\"}");

        var text = result.Value.ToString();

        text.ShouldNotContain("free text");
        text.ShouldNotContain("ref-1");
    }

    [Fact]
    public void InvalidResult_DoesNotCarryAValue()
    {
        Should.Throw<InvalidOperationException>(() => Read("{}").Value);
    }
}
