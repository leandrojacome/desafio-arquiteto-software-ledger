using System.Text.Json;
using Ledger.Domain.Accounts;

namespace Ledger.Domain.Tests.Accounts;

[Trait("Category", "Unit")]
public sealed class AccountIdJsonTests
{
    private const string Canonical = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";

    [Fact]
    public void Serialize_WritesTheHyphenatedText()
    {
        var id = AccountId.From(Canonical).Value;

        var json = JsonSerializer.Serialize(id);

        json.ShouldBe($"\"{Canonical}\"");
    }

    [Fact]
    public void Deserialize_ReadsTheHyphenatedText()
    {
        var id = JsonSerializer.Deserialize<AccountId>($"\"{Canonical}\"");

        id.ToString().ShouldBe(Canonical);
    }

    [Fact]
    public void RoundTrip_ThroughAProperty_KeepsTheId()
    {
        var original = new Holder(AccountId.From(Canonical).Value);

        var copy = JsonSerializer.Deserialize<Holder>(JsonSerializer.Serialize(original));

        copy.ShouldNotBeNull();
        copy.Id.ShouldBe(original.Id);
    }

    [Fact]
    public void Serialize_ThroughAProperty_WritesTheTextAndNotAnObject()
    {
        var json = JsonSerializer.Serialize(new Holder(AccountId.From(Canonical).Value));

        json.ShouldBe($"{{\"Id\":\"{Canonical}\"}}");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("12")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"0192b7c281aa7e04b1d56f0c2a9e8d33\"")]
    [InlineData("\"{0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33}\"")]
    [InlineData("\" 0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33\"")]
    [InlineData("\"+192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33\"")]
    [InlineData("\"0x92b7c2-81aa-7e04-b1d5-6f0c2a9e8d33\"")]
    [InlineData("\"0192b7c2-0xaa-7e04-b1d5-6f0c2a9e8d33\"")]
    public void Deserialize_WithAnythingButTheHyphenatedText_Throws(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<AccountId>(json));
    }

    [Fact]
    public void Serialize_OfTheDefaultInstance_Throws()
    {
        Should.Throw<InvalidOperationException>(() => JsonSerializer.Serialize(default(AccountId)));
    }

    private sealed record Holder(AccountId Id);
}
