using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;

namespace Ledger.Api.IntegrationTests.Correlation;

[Trait("Category", "Concurrency")]
[Trait("Category", "Integration")]
public sealed class CorrelationConcurrencyTests
{
    private const int Requests = 200;

    [Fact]
    public async Task EveryResponseCarriesItsOwnCorrelationIdAndNoLogLineBorrowsAnotherOne()
    {
        using var factory = TestApiFactory.With(new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Debug" });
        using var client = factory.ClientWith(TokenForge.Hmac());

        await ConcurrencySettings.RepeatAsync(async () =>
        {
            var identifiers = Enumerable.Range(0, Requests)
                .Select(index => $"concurrent-{Guid.CreateVersion7():N}-{index}")
                .ToList();

            var returned = await ParallelGate.RunAsync(Requests, async index =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, TestEndpointsStartupFilter.Closed);
                request.Headers.Add("X-Correlation-Id", identifiers[index]);

                using var response = await client.SendAsync(request, CancellationToken.None);

                return response.Headers.GetValues("X-Correlation-Id").ShouldHaveSingleItem();
            });

            returned.ShouldBe(identifiers);
        });

        await ConditionWait.UntilAsync(
            () => factory.Sink.Events.Count(IsSummary) >= Requests,
            TimeSpan.FromSeconds(30),
            "the request summaries being written");

        var summaries = factory.Sink.Events.Where(IsSummary).ToList();

        summaries.ShouldNotBeEmpty();
        summaries.Select(logEvent => Observability.CapturingLogSink.Property(logEvent, "CorrelationId"))
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(summaries.Count);
    }

    private static bool IsSummary(Serilog.Events.LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal);
}
