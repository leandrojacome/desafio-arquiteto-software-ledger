using System.Text.Json;
using Ledger.Api;
using Ledger.Domain.Entries;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ledger.Api.IntegrationTests.Json;

[Trait("Category", "Unit")]
public sealed class LedgerJsonTests
{
    private static readonly JsonSerializerOptions Options = LedgerOptions();

    [Theory]
    [InlineData(EntryType.Credit, "CREDIT")]
    [InlineData(EntryType.Debit, "DEBIT")]
    public void EntryType_RoundTripsAsUppercaseText(EntryType type, string text)
    {
        var json = JsonSerializer.Serialize(new Sample(type), Options);
        var copy = JsonSerializer.Deserialize<Sample>(json, Options);

        json.ShouldBe($"{{\"type\":\"{text}\"}}");
        copy.ShouldNotBeNull();
        copy.Type.ShouldBe(type);
    }

    [Theory]
    [InlineData("{\"type\":1}")]
    [InlineData("{\"type\":2}")]
    [InlineData("{\"type\":0}")]
    [InlineData("{\"type\":42}")]
    [InlineData("{\"type\":-1}")]
    [InlineData("{\"type\":\"1\"}")]
    [InlineData("{\"type\":\"42\"}")]
    [InlineData("{\"type\":\"credit\"}")]
    [InlineData("{\"type\":\"Credit\"}")]
    [InlineData("{\"type\":\"cReDiT\"}")]
    [InlineData("{\"type\":\" CREDIT\"}")]
    [InlineData("{\"type\":\"CREDIT \"}")]
    [InlineData("{\"type\":\"CREDIT,DEBIT\"}")]
    [InlineData("{\"type\":\"\"}")]
    [InlineData("{\"type\":null}")]
    [InlineData("{\"type\":true}")]
    [InlineData("{\"type\":{}}")]
    [InlineData("{\"type\":[]}")]
    public void EntryType_OnRead_RefusesEverythingButTheExactText(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Sample>(json, Options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    public void EntryType_OnWrite_RefusesAnUndefinedValue(int raw)
    {
        var sample = new Sample((EntryType)raw);

        Should.Throw<JsonException>(() => JsonSerializer.Serialize(sample, Options));
    }

    [Fact]
    public void EntryType_OnWrite_RefusesTheDefaultValue()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Serialize(new Sample(default), Options));
    }

    [Fact]
    public void Options_UseCamelCaseAndRefuseUnmappedMembers()
    {
        Options.PropertyNamingPolicy.ShouldBe(JsonNamingPolicy.CamelCase);

        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Sample>("{\"type\":\"CREDIT\",\"extra\":1}", Options));
    }

    [Fact]
    public void Encoder_KeepsTheLatinLettersTheCurlyQuotesAndTheEuroReadable()
    {
        const string text = "Depósito: ação, coração, lançamento, “aspas” e € 80,00, Łódź";

        var json = JsonSerializer.Serialize(new Message(text), Options);

        json.ShouldBe($"{{\"text\":\"{text}\"}}");
        JsonSerializer.Deserialize<Message>(json, Options).ShouldBe(new Message(text));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", """{"text":"\u003Cscript\u003Ealert(1)\u003C/script\u003E"}""")]
    [InlineData("<img src=x onerror=alert(1)>", """{"text":"\u003Cimg src=x onerror=alert(1)\u003E"}""")]
    [InlineData("a & b", """{"text":"a \u0026 b"}""")]
    [InlineData("'aspas'", """{"text":"\u0027aspas\u0027"}""")]
    [InlineData("a + b", """{"text":"a \u002B b"}""")]
    [InlineData("`crase`", """{"text":"\u0060crase\u0060"}""")]
    [InlineData("say \"hi\"", """{"text":"say \u0022hi\u0022"}""")]
    public void Encoder_EscapesTheCharactersThatMeanSomethingInHtml(string text, string expected)
    {
        var json = JsonSerializer.Serialize(new Message(text), Options);

        json.ShouldBe(expected);
        JsonSerializer.Deserialize<Message>(json, Options).ShouldBe(new Message(text));
    }

    [Theory]
    [InlineData("\U0001F600", """{"text":"\uD83D\uDE00"}""")]
    [InlineData("Привет", """{"text":"\u041F\u0440\u0438\u0432\u0435\u0442"}""")]
    [InlineData("a\u2028b", """{"text":"a\u2028b"}""")]
    public void Encoder_EscapesWhatIsOutsideTheLatinRangesAndStillWritesValidJson(string text, string expected)
    {
        var json = JsonSerializer.Serialize(new Message(text), Options);

        json.ShouldBe(expected);
        JsonSerializer.Deserialize<Message>(json, Options).ShouldBe(new Message(text));
    }

    private static JsonSerializerOptions LedgerOptions()
    {
        var services = new ServiceCollection().AddLedgerJson();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }

    private sealed record Sample(EntryType Type);

    private sealed record Message(string Text);
}
