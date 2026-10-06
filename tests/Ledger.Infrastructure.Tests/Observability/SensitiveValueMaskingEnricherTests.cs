using Ledger.Infrastructure.Observability;
using Ledger.Infrastructure.Tests.Observability.Support;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class SensitiveValueMaskingEnricherTests
{
    private static readonly string[] Sequence = ["ok", "12345678909"];

    private readonly CollectingLogSink _sink = new();

    private Logger Logger() =>
        new LoggerConfiguration()
            .Enrich.With<SensitiveValueMaskingEnricher>()
            .WriteTo.Sink(_sink)
            .CreateLogger();

    [Fact]
    public void Enrich_StringPropertyWithABearerToken_IsMaskedInThePropertyAndInTheRenderedMessage()
    {
        using var logger = Logger();

        logger.Information("Header was {Header}", "Bearer abc.def.ghi");

        var json = _sink.AllJson();
        json.ShouldNotContain("abc.def.ghi");
        json.ShouldContain("Bearer ***");
    }

    [Fact]
    public void Enrich_ConnectionStringInAProperty_LosesItsPassword()
    {
        using var logger = Logger();

        logger.Warning("Failed to connect with {Connection}", "Host=db;Username=ledger;Password=correct-horse");

        var json = _sink.AllJson();
        json.ShouldNotContain("correct-horse");
        json.ShouldContain("Password=***");
        json.ShouldContain("Host=db");
    }

    [Fact]
    public void Enrich_FormattedDocumentInsideAStructure_IsMasked()
    {
        using var logger = Logger();

        logger.Information("{@Holder}", new { Name = "n", Note = "cpf 123.456.789-09" });

        _sink.AllJson().ShouldNotContain("123.456.789-09");
    }

    [Fact]
    public void Enrich_DocumentInsideASequenceAndADictionary_IsMasked()
    {
        using var logger = Logger();

        logger.Information(
            "{@Items} {@Map}",
            Sequence,
            new Dictionary<string, string> { ["k"] = "Bearer token-value" });

        var json = _sink.AllJson();
        json.ShouldNotContain("12345678909");
        json.ShouldNotContain("token-value");
    }

    [Fact]
    public void Enrich_PropertiesWithoutSensitiveText_AreKeptAsTheSameInstances()
    {
        using var logger = Logger();

        logger.Information("{Route} {Status} {Elapsed}", "/v1/accounts/{accountId}", 200, 12.4);

        var properties = _sink.Events.Single().Properties;
        properties["Route"].ShouldBeOfType<ScalarValue>().Value.ShouldBe("/v1/accounts/{accountId}");
        properties["Status"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(200);
        properties["Elapsed"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(12.4);
    }

    [Fact]
    public void Enrich_NonStringScalars_AreNeverTouched()
    {
        using var logger = Logger();

        logger.Information("{Number}", 12345678909L);

        _sink.Events.Single().Properties["Number"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(12345678909L);
    }
}
