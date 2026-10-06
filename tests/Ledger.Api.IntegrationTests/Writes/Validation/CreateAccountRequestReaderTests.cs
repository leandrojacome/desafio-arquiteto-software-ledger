using System.Text;
using Ledger.Api.Validation;

namespace Ledger.Api.IntegrationTests.Writes.Validation;

[Trait("Category", "Unit")]
public sealed class CreateAccountRequestReaderTests
{
    private const string ValidCpf = "123.456.789-09";

    private static ReadResult<CreateAccountInput> Read(string json) =>
        CreateAccountRequestReader.Read(Encoding.UTF8.GetBytes(json));

    private static string WithLimit(string token) =>
        $"{{\"holderDocument\":\"{ValidCpf}\",\"currency\":\"BRL\",\"overdraftLimit\":{token}}}";

    private static (string Field, string Reason) Single(ReadResult<CreateAccountInput> result)
    {
        result.IsValid.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();

        return (issue.Field, issue.Reason);
    }

    [Fact]
    public void ExampleOfTheContract_IsRead()
    {
        var result = Read($"{{\"holderDocument\":\"{ValidCpf}\",\"currency\":\"BRL\",\"overdraftLimit\":\"0.00\"}}");

        result.IsValid.ShouldBeTrue();
        result.Value.HolderDocument.Normalized.ShouldBe("12345678909");
        result.Value.Currency.ShouldBe("BRL");
        result.Value.OverdraftLimit.ToDecimalString().ShouldBe("0.00");
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"overdraftLimit\":null")]
    public void OverdraftLimit_WhenAbsentOrNull_IsZero(string suffix)
    {
        var result = Read($"{{\"holderDocument\":\"{ValidCpf}\",\"currency\":\"BRL\"{suffix}}}");

        result.IsValid.ShouldBeTrue();
        result.Value.OverdraftLimit.ToDecimalString().ShouldBe("0.00");
    }

    [Theory]
    [InlineData("\"0\"", "0.00")]
    [InlineData("\"0.00\"", "0.00")]
    [InlineData("\"50\"", "50.00")]
    [InlineData("\"50.5\"", "50.50")]
    [InlineData("\"999999999.99\"", "999999999.99")]
    public void OverdraftLimit_WithAcceptedText_IsReadWithTwoDecimalPlaces(string token, string expected)
    {
        var result = Read(WithLimit(token));

        result.IsValid.ShouldBeTrue();
        result.Value.OverdraftLimit.ToDecimalString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("\"500,00\"")]
    [InlineData("\"R$ 500.00\"")]
    [InlineData("\"1.500,00\"")]
    public void OverdraftLimit_WithACurrencySymbolOrABrazilianSeparator_ExplainsTheDecimalTextOfTheContract(string token)
    {
        var issue = Read(WithLimit(token)).Issues.ShouldHaveSingleItem();

        issue.Field.ShouldBe("overdraftLimit");
        issue.Reason.ShouldBe("INVALID_FORMAT");
        issue.Message.ShouldBe(
            "O campo 'overdraftLimit' deve ser um texto decimal com ponto, como '80.00', sem símbolo de moeda nem separador de milhar.");
    }

    [Theory]
    [InlineData("50", "INVALID_FORMAT")]
    [InlineData("\"abc\"", "INVALID_FORMAT")]
    [InlineData("\"1e3\"", "INVALID_FORMAT")]
    [InlineData("\"80,00\"", "INVALID_FORMAT")]
    [InlineData("\".5\"", "INVALID_FORMAT")]
    [InlineData("\"80.\"", "INVALID_FORMAT")]
    [InlineData("\"-1.00\"", "OUT_OF_RANGE")]
    [InlineData("\"-0.00\"", "OUT_OF_RANGE")]
    [InlineData("\"1000000000.00\"", "OUT_OF_RANGE")]
    [InlineData("\"10.001\"", "TOO_MANY_DECIMALS")]
    public void OverdraftLimit_WithRejectedValue_ReportsTheReasonOfTheFirstRuleThatFails(string token, string reason)
    {
        Single(Read(WithLimit(token))).ShouldBe(("overdraftLimit", reason));
    }

    [Theory]
    [InlineData("{\"currency\":\"BRL\"}", "holderDocument", "REQUIRED")]
    [InlineData("{\"holderDocument\":null,\"currency\":\"BRL\"}", "holderDocument", "REQUIRED")]
    [InlineData("{\"holderDocument\":12345678909,\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-00\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"111.111.111-11\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"1234567890\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"abc.def.ghi-jk\",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09 \",\"currency\":\"BRL\"}", "holderDocument", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\"}", "currency", "REQUIRED")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":null}", "currency", "REQUIRED")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"brl\"}", "currency", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"BR\"}", "currency", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":1}", "currency", "INVALID_FORMAT")]
    [InlineData("{\"holderDocument\":\"123.456.789-09\",\"currency\":\"EUR\"}", "currency", "UNSUPPORTED_CURRENCY")]
    public void RequiredFields_WithAProblem_ReportTheReason(string json, string field, string reason)
    {
        Single(Read(json)).ShouldBe((field, reason));
    }

    [Theory]
    [InlineData("12.345.678/0001-95", "12345678000195")]
    [InlineData("12ABC34501DE35", "12ABC34501DE35")]
    [InlineData("12abc34501de35", "12ABC34501DE35")]
    [InlineData("12345678909", "12345678909")]
    public void HolderDocument_OfEitherKind_IsNormalized(string raw, string normalized)
    {
        var result = Read($"{{\"holderDocument\":\"{raw}\",\"currency\":\"BRL\"}}");

        result.IsValid.ShouldBeTrue();
        result.Value.HolderDocument.Normalized.ShouldBe(normalized);
    }

    [Theory]
    [InlineData("clientId")]
    [InlineData("accountId")]
    [InlineData("holderName")]
    [InlineData("id")]
    public void UnknownProperty_IsReportedWithItsName(string name)
    {
        var result = Read($"{{\"holderDocument\":\"{ValidCpf}\",\"currency\":\"BRL\",\"{name}\":\"x\"}}");

        Single(result).ShouldBe((name, "UNKNOWN_FIELD"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("1")]
    [InlineData("{broken")]
    [InlineData("{\"currency\":\"BRL\",\"currency\":\"BRL\"}")]
    public void BodyThatIsNotASingleObject_IsOneInvalidJsonIssueOnTheRoot(string json)
    {
        Single(Read(json)).ShouldBe(("$", "INVALID_JSON"));
    }

    [Fact]
    public void EveryProblem_IsAccumulatedInOneList()
    {
        var result = Read("{\"holderDocument\":\"123\",\"currency\":\"EUR\",\"overdraftLimit\":\"-5\",\"clientId\":\"x\"}");

        result.Issues.Select(issue => (issue.Field, issue.Reason)).ShouldBe(
        [
            ("holderDocument", "INVALID_FORMAT"),
            ("currency", "UNSUPPORTED_CURRENCY"),
            ("overdraftLimit", "OUT_OF_RANGE"),
            ("clientId", "UNKNOWN_FIELD")
        ]);
    }

    [Fact]
    public void Messages_NeverRepeatTheReceivedValues()
    {
        const string canary = "987.654.321-01";
        var result = Read($"{{\"holderDocument\":\"{canary}\",\"currency\":\"{canary}\",\"overdraftLimit\":\"{canary}\"}}");

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldAllBe(issue => !issue.Message.Contains("987", StringComparison.Ordinal));
        result.Issues.ShouldAllBe(issue => !issue.Message.Contains("321", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidInput_NeverPrintsTheDocumentInToString()
    {
        var result = Read($"{{\"holderDocument\":\"{ValidCpf}\",\"currency\":\"BRL\"}}");

        var text = result.Value.ToString();

        text.ShouldNotContain("12345678909");
        text.ShouldNotContain("123.456.789-09");
        text.ShouldNotContain("456");
    }
}
