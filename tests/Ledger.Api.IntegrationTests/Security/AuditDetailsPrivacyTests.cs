using System.Globalization;
using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Accounts;
using Ledger.Application.Audit;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Audit;
using Ledger.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class AuditDetailsPrivacyTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string[]> AllowedKeys = new(StringComparer.Ordinal)
    {
        ["account.created"] = [],
        ["authorization.denied_write"] = ["route", "requiredScope", "reason"],
        ["pii.decrypted"] = ["purpose", "accounts"],
        ["pii.rewrapped"] = ["fromVersion", "toVersion", "accounts", "failed"],
        ["keys.version_activated"] = ["version"]
    };

    [DockerFact]
    public async Task EveryRowOfAFullRun_UsesOnlyTheClosedKeysAndHoldsNoDocumentBlobOrIndex()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var telemetry = new RecordingSecurityTelemetry();
        var versionOne = PiiKeys.Protector(1, 1);
        var versionTwo = PiiKeys.Protector(2, 1, 2);
        var accounts = await AccountSeed.InsertManyAsync(api, versionOne, 20);
        var apiTrail = new PostgresAuditTrail(database.Connections, PostgresSource.Write, telemetry);
        var workerTrail = new PostgresAuditTrail(database.Connections, PostgresSource.Worker, telemetry);

        await apiTrail.RecordAsync(AuditEvents.AccountCreated("pix-core", accounts[0].Id, "corr-1"), CancellationToken.None);
        await using var auditor = new DeniedWriteAuditor(
            apiTrail,
            telemetry,
            Options.Create(new DeniedWriteAuditOptions()),
            new FakeTimeProvider(),
            NullLogger<DeniedWriteAuditor>.Instance);
        auditor.Record("reader-only", accounts[1].Id, "corr-2", "POST /v1/accounts/{accountId}/entries", DeniedWriteReason.InsufficientScope);
        await ConditionWait.UntilAsync(() => auditor.PendingWrites == 0, TimeSpan.FromSeconds(30), "denial written");
        await new RecordKeyActivationHandler(
                PiiKeys.Provider(2, 1, 2),
                workerTrail,
                new Uuid7IdGenerator(TimeProvider.System),
                NullLogger<RecordKeyActivationHandler>.Instance)
            .HandleAsync(CancellationToken.None);
        await new RewrapAccountsHandler(
                new PostgresAccountKeyRewrapper(database.Connections, Options.Create(database.Settings), telemetry),
                versionTwo,
                PiiKeys.Provider(2, 1, 2),
                telemetry,
                NullLogger<RewrapAccountsHandler>.Instance)
            .HandleAsync(new RewrapAccountsCommand(2, 10, "0192b7c281aa7e04b1d56f0c2a9e8d33"), CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var rows = await ReadRowsAsync(admin);

        rows.Select(row => row.EventType).Distinct().Order().ShouldBe(AllowedKeys.Keys.Order());

        foreach (var row in rows)
        {
            using var details = JsonDocument.Parse(row.Details);
            var keys = details.RootElement.EnumerateObject().Select(property => property.Name).ToList();

            keys.ShouldBeSubsetOf(AllowedKeys[row.EventType]);
            keys.Count.ShouldBe(AllowedKeys[row.EventType].Length);
        }

        var everything = string.Join('\n', rows.Select(row => row.WholeRow));

        foreach (var account in accounts)
        {
            everything.ShouldNotContain(account.Document.Normalized, Case.Sensitive);
            everything.ShouldNotContain(FormatCpf(account.Document.Normalized), Case.Sensitive);
        }

        foreach (var account in accounts)
        {
            var stored = await AccountSeed.ReadAsync(database, account.Id);
            var blob = stored.Encrypted.ShouldNotBeNull();
            var index = stored.BlindIndex.ShouldNotBeNull();

            everything.ShouldNotContain(Convert.ToBase64String(blob), Case.Sensitive);
            everything.ShouldNotContain(Convert.ToHexString(blob), Case.Insensitive);
            everything.ShouldNotContain(Convert.ToHexString(index), Case.Insensitive);
        }

        everything.ShouldNotContain(TestConfiguration.PiiEncryptionKeyOne, Case.Sensitive);
        everything.ShouldNotContain(TestConfiguration.PiiBlindIndexKeyTwo, Case.Sensitive);
    }

    private static string FormatCpf(string digits) =>
        string.Create(CultureInfo.InvariantCulture, $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}");

    private static async Task<List<TrailRow>> ReadRowsAsync(NpgsqlConnection admin)
    {
        await using var command = new NpgsqlCommand(
            "SELECT event_type, details::text, audit_log::text FROM audit_log ORDER BY id",
            admin);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var rows = new List<TrailRow>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            rows.Add(new TrailRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private sealed record TrailRow(string EventType, string Details, string WholeRow);
}
