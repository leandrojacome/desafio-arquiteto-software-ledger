using Ledger.Infrastructure.Observability;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class SensitiveTextMaskerTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi_123", "Authorization: Bearer ***")]
    [InlineData("bearer    QWxhZGRpbjpvcGVu==", "Bearer ***")]
    [InlineData("token was Bearer abc and then more", "token was Bearer *** and then more")]
    public void Apply_BearerToken_IsMasked(string input, string expected)
    {
        SensitiveTextMasker.Apply(input).ShouldBe(expected);
    }

    [Fact]
    public void Apply_JsonWebToken_IsMasked()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var masked = SensitiveTextMasker.Apply($"rejected {jwt} for the client");

        masked.ShouldBe("rejected *** for the client");
    }

    [Theory]
    [InlineData("Host=db;Username=u;Password=s3cret;Database=ledger", "Host=db;Username=u;Password=***;Database=ledger")]
    [InlineData("Host=db;pwd = hunter2", "Host=db;pwd = ***")]
    [InlineData("endpoint;ApiKey=abcdef;x=1", "endpoint;ApiKey=***;x=1")]
    [InlineData("Api_Key=abcdef", "Api_Key=***")]
    [InlineData("secret=value", "secret=***")]
    public void Apply_SecretsInsideConnectionStrings_AreMasked(string input, string expected)
    {
        SensitiveTextMasker.Apply(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("holder 123.456.789-09 refused", "holder *** refused")]
    [InlineData("123.456.789-09", "***")]
    [InlineData("pj 12.345.678/0001-95 refused", "pj *** refused")]
    [InlineData("pj 12.ABC.345/01DE-35 refused", "pj *** refused")]
    public void Apply_FormattedHolderDocuments_AreMasked(string input, string expected)
    {
        SensitiveTextMasker.Apply(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("12345678909")]
    [InlineData("12345678000195")]
    public void Apply_ValueThatIsOnlyADocumentNumber_IsMasked(string input)
    {
        SensitiveTextMasker.Apply(input).ShouldBe("***");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ledger-api")]
    [InlineData("0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("1760000000000")]
    [InlineData("12345678909 apples")]
    [InlineData("/v1/accounts/{accountId}/entries")]
    [InlineData("INSUFFICIENT_FUNDS")]
    [InlineData("HTTP GET /health/ready responded 200 in 1.8 ms")]
    [InlineData("a password was requested")]
    public void Apply_OrdinaryText_IsLeftAlone(string input)
    {
        SensitiveTextMasker.Apply(input).ShouldBe(input);
    }
}
