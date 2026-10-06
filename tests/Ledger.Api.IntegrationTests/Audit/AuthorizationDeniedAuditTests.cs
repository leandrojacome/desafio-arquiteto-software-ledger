using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Security;
using Ledger.Api.IntegrationTests.Writes.Support;
using Ledger.Application.Abstractions;
using Ledger.Application.Audit;
using Ledger.Infrastructure.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Audit;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Security")]
[Trait("Category", "Integration")]
public sealed class AuthorizationDeniedAuditTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [DockerFact]
    public async Task AReadOnlyTokenOnTheWriteRoute_LeavesOneDeniedWriteRowWithTheTemplateRoute()
    {
        using var factory = new DatabaseLedgerApiFactory(postgres);
        var client = $"reader-{Guid.NewGuid():N}";
        var account = Guid.NewGuid();
        using var http = factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: client));

        using var response = await Post(http, $"/v1/accounts/{account}/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        await WaitForRowsAsync(admin, client, 1);

        (await Scalar(admin, client, "outcome")).ShouldBe("DENIED");
        (await Scalar(admin, client, "details ->> 'route'")).ShouldBe("POST /v1/accounts/{accountId}/entries");
        (await Scalar(admin, client, "details ->> 'requiredScope'")).ShouldBe("ledger.write");
        (await Scalar(admin, client, "details ->> 'reason'")).ShouldBe("insufficient_scope");
        (await Scalar(admin, client, "account_id::text")).ShouldBe(account.ToString());
        (await Scalar(admin, client, "correlation_id")).ShouldNotBeNullOrWhiteSpace();
        (await Scalar(admin, client, "details::text")).ShouldNotContain(account.ToString());
    }

    [DockerFact]
    public async Task ARouteWithAnAccountThatIsNotAGuid_LeavesTheRowWithoutAnAccount()
    {
        using var factory = new DatabaseLedgerApiFactory(postgres);
        var client = $"reader-{Guid.NewGuid():N}";
        using var http = factory.Authenticated(TestTokenFactory.Create(scope: "ledger.read", clientId: client));

        using var response = await Post(http, "/v1/accounts/not-a-guid/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        await WaitForRowsAsync(admin, client, 1);

        (await Scalar(admin, client, "account_id::text")).ShouldBeEmpty();
        (await Scalar(admin, client, "details ->> 'route'")).ShouldBe("POST /v1/accounts/{accountId}/entries");
    }

    [DockerFact]
    public async Task ARequestWithoutToken_LeavesNoDeniedWriteRow()
    {
        using var factory = new DatabaseLedgerApiFactory(postgres);
        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        var before = await CountAllAsync(admin);
        using var http = factory.CreateClient();

        using var response = await Post(http, $"/v1/accounts/{Guid.NewGuid()}/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        (await CountAllAsync(admin)).ShouldBe(before);
    }

    [DockerFact]
    public async Task AClientOutsideTheProvisioningList_LeavesTheNotProvisioningClientRow()
    {
        await using var factory = new WriteApiFactory(
            postgres,
            new Dictionary<string, string?> { ["Authorization:AccountProvisioningClients:0"] = "billing-core" });
        var client = $"pix-{Guid.NewGuid():N}";
        var api = new WriteClient(factory.CreateClient());

        var response = await api.PostAccountAsync(options: WriteAs("ledger.write", client));

        response.StatusCode.ShouldBe(403, response.Body);

        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        await WaitForRowsAsync(admin, client, 1);

        (await Scalar(admin, client, "details ->> 'reason'")).ShouldBe("not_provisioning_client");
        (await Scalar(admin, client, "details ->> 'route'")).ShouldBe("POST /v1/accounts");
        (await Scalar(admin, client, "account_id::text")).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task ATokenWithoutClientId_LeavesNoDeniedWriteRow()
    {
        await using var factory = new WriteApiFactory(postgres);
        await using var admin = await postgres.OpenConnectionAsync(CancellationToken.None);
        var before = await CountAllAsync(admin);
        var api = new WriteClient(factory.CreateClient());

        var response = await api.PostEntryAsync(
            Guid.NewGuid().ToString(),
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            WriteAs("ledger.write", null));

        response.StatusCode.ShouldBe(403, response.Body);

        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        (await CountAllAsync(admin)).ShouldBe(before);
    }

    [DockerFact]
    public async Task WhenTheTrailThrows_TheDenialIsAnsweredTheSameWayAndTheFailureIsCounted()
    {
        using var skipped = new TaggedCounter("Ledger", "ledger.audit.skipped");
        var failures = new LogCapture<DeniedWriteAuditor>();
        var healthy = await DenyAsync(trail: null);
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: services =>
            {
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail>(new ThrowingTrail());
                services.AddSingleton<ILogger<DeniedWriteAuditor>>(failures);
            });
        var api = new WriteClient(factory.CreateClient());

        var response = await DeniedWriteAsync(api, "throwing-trail");

        response.StatusCode.ShouldBe(403, response.Body);
        response.Header("WWW-Authenticate").ShouldBe(healthy.Header("WWW-Authenticate"));
        response.ContentType.ShouldBe(healthy.ContentType);
        response.Text("code").ShouldBe("FORBIDDEN");
        response.Text("title").ShouldBe(healthy.Text("title"));

        await ConditionWait.UntilAsync(
            () => failures.Events.Count > 0 && skipped.TotalWhere("reason", "write_failed") > 0,
            Patience,
            "the failure of the trail to be logged and counted");

        failures.Events.ShouldHaveSingleItem().Id.ShouldBe(6004);
        skipped.TotalWhere("reason", "write_failed").ShouldBe(1);
    }

    [DockerFact]
    public async Task WhenTheTrailIsSlow_TheDenialDoesNotWaitForIt()
    {
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: services =>
            {
                services.RemoveAll<IAuditTrail>();
                services.AddSingleton<IAuditTrail>(new SlowTrail(TimeSpan.FromSeconds(2)));
            });
        var api = new WriteClient(factory.CreateClient());
        var warmup = await DeniedWriteAsync(api, "slow-trail");
        var watch = Stopwatch.StartNew();

        var response = await DeniedWriteAsync(api, "slow-trail");

        watch.Stop();

        warmup.StatusCode.ShouldBe(403, warmup.Body);
        response.StatusCode.ShouldBe(403, response.Body);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(1500));
    }

    private static WriteRequestOptions WriteAs(string scope, string? clientId) =>
        new() { Token = TestTokenFactory.Create(scope: scope, clientId: clientId) };

    private static Task<ApiResponse> DeniedWriteAsync(WriteClient api, string client) =>
        api.PostEntryAsync(
            Guid.NewGuid().ToString(),
            WriteClient.EntryBody("CREDIT", "1.00"),
            WriteClient.NewKey(),
            WriteAs("ledger.read", client));

    private async Task<ApiResponse> DenyAsync(IAuditTrail? trail)
    {
        await using var factory = new WriteApiFactory(
            postgres,
            configureServices: services =>
            {
                if (trail is not null)
                {
                    services.RemoveAll<IAuditTrail>();
                    services.AddSingleton(trail);
                }
            });

        return await DeniedWriteAsync(new WriteClient(factory.CreateClient()), "healthy-trail");
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string route)
    {
        using var body = new StringContent("{}");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return await client.PostAsync(route, body, CancellationToken.None);
    }

    private static async Task WaitForRowsAsync(NpgsqlConnection admin, string client, long expected)
    {
        await ConditionWait.UntilAsync(
            async () => await CountAsync(admin, client) >= expected,
            Patience,
            "the denied write to reach the audit trail");

        (await CountAsync(admin, client)).ShouldBe(expected);
    }

    private static async Task<long> CountAsync(NpgsqlConnection admin, string client)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM audit_log WHERE event_type = 'authorization.denied_write' AND client_id = @client",
            admin);
        command.Parameters.AddWithValue("client", client);

        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<long> CountAllAsync(NpgsqlConnection admin) =>
        await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log WHERE event_type = 'authorization.denied_write'");

    [SuppressMessage("Security", "CA2100",
        Justification = "Test helper: the expression is always a literal written in this file.")]
    private static async Task<string> Scalar(NpgsqlConnection admin, string client, string expression)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT COALESCE(({expression})::text, '') FROM audit_log WHERE event_type = 'authorization.denied_write' AND client_id = @client",
            admin);
        command.Parameters.AddWithValue("client", client);

        return (string)(await command.ExecuteScalarAsync(CancellationToken.None))!;
    }

    private sealed class ThrowingTrail : IAuditTrail
    {
        public Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The audit table is not reachable.");

        public Task<bool> ContainsAsync(string eventType, string detailName, string detailValue, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The audit table is not reachable.");
    }

    private sealed class SlowTrail(TimeSpan delay) : IAuditTrail
    {
        public async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
        }

        public Task<bool> ContainsAsync(string eventType, string detailName, string detailValue, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
