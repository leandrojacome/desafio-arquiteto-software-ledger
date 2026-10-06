using System.Net.Http.Headers;
using System.Text;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace Ledger.Api.IntegrationTests.Logging;

[Trait("Category", "Integration")]
[Trait("Category", "Security")]
[Collection(OtelEnvironment.Collection)]
public sealed class LogLeakTests
{
    private const string BodyCanary = "BODY-CANARY-8c41f7a2-note-do-not-log";
    private const string PasswordCanary = "PASSWORD-CANARY-b9d3e1";
    private const string SensitiveCanary = "SENSITIVE-CANARY-5f20c8";

    private static IReadOnlyList<string> Pieces(string token)
    {
        return [token, .. token.Split('.'), $"Bearer {token}"];
    }

    [Fact]
    public async Task Request_WithAValidBearerToken_NeverPutsTheTokenOrAnyPartOfItInTheLogs()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        var token = TestTokenFactory.Create();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var protectedRoute = await client.GetAsync("/v1/accounts", CancellationToken.None);
        using var unknownRoute = await client.GetAsync("/does-not-exist", CancellationToken.None);
        using var live = await client.GetAsync("/health/live", CancellationToken.None);

        sink.Events.ShouldNotBeEmpty();
        var everything = sink.Everything();
        foreach (var piece in Pieces(token))
        {
            everything.ShouldNotContain(piece);
        }
    }

    [Fact]
    public async Task Request_WithAnInvalidBearerToken_NeverPutsTheTokenOrAnyPartOfItInTheLogs()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        var token = TestTokenFactory.Create(signingKey: "a-different-signing-key-with-more-than-32-chars");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/accounts", CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
        sink.Events.ShouldNotBeEmpty();
        var everything = sink.Everything();
        foreach (var piece in Pieces(token))
        {
            everything.ShouldNotContain(piece);
        }
    }

    [Fact]
    public async Task Request_WithABodyThatCarriesMarkedText_NeverPutsTheBodyInTheLogs()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        using var client = factory.Authenticated();
        using var content = new StringContent(
            $"{{\"holderDocument\":\"{BodyCanary}\",\"description\":\"{BodyCanary}\",\"amount\":\"123456.78\"}}",
            Encoding.UTF8,
            "application/json");

        using var created = await client.PostAsync("/v1/accounts", content, CancellationToken.None);
        using var malformedContent = new StringContent("{ not json " + BodyCanary, Encoding.UTF8, "application/json");
        using var malformed = await client.PostAsync("/v1/accounts", malformedContent, CancellationToken.None);

        sink.Events.ShouldNotBeEmpty();
        sink.Everything().ShouldNotContain(BodyCanary);
        sink.Everything().ShouldNotContain("123456.78");
    }

    [Fact]
    public async Task DatabaseFailure_WithAConnectionStringThatCarriesAPassword_NeverPutsItInTheLogs()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        var settings = TestConfiguration.ForPostgres("127.0.0.1", 1, "ledger", "ledger_api", PasswordCanary);
        using var factory = new ObservabilityApiFactory(sink, settings);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);
        using var second = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.ServiceUnavailable);
        sink.Events.ShouldNotBeEmpty();
        var everything = sink.Everything();
        everything.ShouldNotContain(PasswordCanary);
        everything.ShouldNotContain("Password=");
    }

    [Fact]
    public async Task DatabaseFailure_ReportedInTheBodyOfTheResponse_NeverCarriesTheConnectionDetails()
    {
        using var clean = OtelEnvironment.Clean();
        var settings = TestConfiguration.ForPostgres("127.0.0.1", 1, "ledger", "ledger_api", PasswordCanary);
        using var factory = new ObservabilityApiFactory(new CapturingLogSink(), settings);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync(CancellationToken.None);

        body.ShouldNotContain(PasswordCanary);
        body.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public void LoggedObject_WithASensitiveMember_IsRenderedAsMasked()
    {
        using var clean = OtelEnvironment.Clean();
        var sink = new CapturingLogSink();
        using var factory = new ObservabilityApiFactory(sink);
        var logger = factory.Services.GetRequiredService<Serilog.ILogger>();

        logger.Warning("Payload was {@Payload}", new Payload("visible-value", SensitiveCanary));

        var logEvent = sink.Events.Single(candidate => candidate.MessageTemplate.Text.StartsWith("Payload was", StringComparison.Ordinal));
        var structure = logEvent.Properties["Payload"].ShouldBeOfType<StructureValue>();
        sink.Everything().ShouldNotContain(SensitiveCanary);
        RenderedMember(structure, "Secret").ShouldBe("\"***\"");
        RenderedMember(structure, "Visible").ShouldBe("\"visible-value\"");
    }

    private static string RenderedMember(StructureValue structure, string name)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        structure.Properties.Single(property => property.Name == name).Value
            .Render(writer, null, System.Globalization.CultureInfo.InvariantCulture);

        return writer.ToString();
    }

    private sealed record Payload(string Visible, [property: Sensitive] string Secret);
}
