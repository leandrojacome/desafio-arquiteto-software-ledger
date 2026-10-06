using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Observability;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Domain.Accounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Api.IntegrationTests.Security;

[Trait("Category", "Security")]
[Trait("Category", "Integration")]
[Collection(OtelEnvironment.Collection)]
public sealed class DeniedWriteRecordingTests : IDisposable
{
    private const string Account = "0192b7c2-81aa-7e04-b1d5-6f0c2a9e8d33";

    private readonly EnvironmentVariableScope _environment = OtelEnvironment.Clean();
    private readonly RecordingDeniedWriteAuditor _auditor = new();
    private readonly ObservabilityApiFactory _factory;

    public DeniedWriteRecordingTests()
    {
        _factory = new ObservabilityApiFactory(
            new CapturingLogSink(),
            new Dictionary<string, string?> { ["Authorization:AccountProvisioningClients:0"] = "billing-core" },
            services =>
            {
                services.RemoveAll<IDeniedWriteAuditor>();
                services.AddSingleton<IDeniedWriteAuditor>(_auditor);
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _environment.Dispose();
    }

    [Fact]
    public async Task AReadOnlyTokenOnAWriteRoute_RecordsTheTemplateRouteAndTheAccount()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting"));

        using var response = await Post(client, $"/v1/accounts/{Account}/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var denial = _auditor.Denials.ShouldHaveSingleItem();

        denial.ClientId.ShouldBe("reporting");
        denial.AccountId.ShouldBe(AccountId.From(Account).Value);
        denial.Route.ShouldBe("POST /v1/accounts/{accountId}/entries");
        denial.Reason.ShouldBe(DeniedWriteReason.InsufficientScope);
        denial.CorrelationId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TheRecordedCorrelationId_IsTheOneOfTheResponse()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting"));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/accounts/{Account}/entries")
        {
            Content = new StringContent("{}")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Correlation-Id", "audit-correlation-0001");

        using var response = await client.SendAsync(request, CancellationToken.None);

        _auditor.Denials.ShouldHaveSingleItem().CorrelationId.ShouldBe("audit-correlation-0001");
    }

    [Fact]
    public async Task ARouteWithAnAccountThatIsNotAGuid_RecordsNoAccount()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: "reporting"));

        using var response = await Post(client, "/v1/accounts/not-a-guid/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var denial = _auditor.Denials.ShouldHaveSingleItem();

        denial.AccountId.ShouldBeNull();
        denial.Route.ShouldBe("POST /v1/accounts/{accountId}/entries");
    }

    [Fact]
    public async Task AClientOutsideTheProvisioningList_RecordsTheProvisioningReason()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.write", clientId: "pix-core"));

        using var response = await Post(client, "/v1/accounts");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var denial = _auditor.Denials.ShouldHaveSingleItem();

        denial.Route.ShouldBe("POST /v1/accounts");
        denial.AccountId.ShouldBeNull();
        denial.Reason.ShouldBe(DeniedWriteReason.NotProvisioningClient);
    }

    [Fact]
    public async Task ADeniedRead_IsNotAudited()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.write", clientId: "writer"));

        using var response = await client.GetAsync($"/v1/accounts/{Account}/balance", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _auditor.Denials.ShouldBeEmpty();
    }

    [Fact]
    public async Task ATokenWithoutClientId_IsNotAudited()
    {
        using var client = _factory.Authenticated(TestTokenFactory.Create(scope: "ledger.write", clientId: null));

        using var response = await Post(client, $"/v1/accounts/{Account}/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _auditor.Denials.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARequestWithoutToken_IsNotAudited()
    {
        using var client = _factory.CreateClient();

        using var response = await Post(client, $"/v1/accounts/{Account}/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _auditor.Denials.ShouldBeEmpty();
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }

    private sealed class RecordingDeniedWriteAuditor : IDeniedWriteAuditor
    {
        private readonly ConcurrentQueue<Denial> _denials = new();

        public IReadOnlyList<Denial> Denials => [.. _denials];

        public void Record(
            string clientId,
            AccountId? accountId,
            string correlationId,
            string route,
            DeniedWriteReason reason)
        {
            _denials.Enqueue(new Denial(clientId, accountId, correlationId, route, reason));
        }
    }

    private sealed record Denial(
        string ClientId,
        AccountId? AccountId,
        string CorrelationId,
        string Route,
        DeniedWriteReason Reason);
}
