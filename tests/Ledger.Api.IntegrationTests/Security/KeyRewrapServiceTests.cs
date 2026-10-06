using System.Diagnostics.CodeAnalysis;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(MessagingCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class KeyRewrapServiceTests(PostgresFixture postgres, RabbitMqFixture broker)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [DockerFact]
    public async Task TheWorker_RewrapsTheAccountsOfAnOlderKeyVersion_AndRecordsTheActivationOfTheNewOne()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var admin = await scenario.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        var versionOne = PiiKeys.Protector(1, 1);
        var versionTwo = PiiKeys.Protector(2, 1, 2);
        var accounts = await AccountSeed.InsertManyAsync(admin, versionOne, 25);

        scenario.StartWorker(TwoVersions());

        await ConditionWait.UntilAsync(
            async () => await CountAtVersionAsync(admin, 2) == accounts.Count,
            Patience,
            "every account to be rewrapped to the active key version");

        foreach (var account in accounts)
        {
            var stored = await AccountSeed.ReadAsync(admin, account.Id);

            stored.KeyVersion.ShouldBe(2);
            versionTwo.Unprotect(stored.Encrypted.ShouldNotBeNull(), account.Id).Value.Normalized
                .ShouldBe(account.Document.Normalized);
        }

        await ConditionWait.UntilAsync(
            async () => await AuditCountAsync(admin, "keys.version_activated") == 1,
            Patience,
            "the activation of key version 2 to be recorded");

        (await AuditRewrappedAccountsAsync(admin)).ShouldBe(accounts.Count);
    }

    [DockerFact]
    public async Task TheWorker_WhenARestartFindsTheActivationAlreadyRecorded_DoesNotRecordItAgain()
    {
        await using var scenario = await WorkerScenario.CreateAsync(postgres, broker, withProbe: false);
        await using var admin = await scenario.Database.OpenAdministrativeConnectionAsync(CancellationToken.None);
        var first = scenario.StartWorker(TwoVersions());

        await ConditionWait.UntilAsync(
            async () => await AuditCountAsync(admin, "keys.version_activated") == 1,
            Patience,
            "the first activation to be recorded");

        await scenario.StopWorkerAsync(first);

        var second = scenario.StartWorker(TwoVersions());
        var usage = second.Services.GetRequiredService<KeyUsageHolder>();

        await ConditionWait.UntilAsync(
            () => usage.AccountsBelowActive is not null,
            Patience,
            "the restarted worker to finish its first rewrap pass");

        (await AuditCountAsync(admin, "keys.version_activated")).ShouldBe(1);
    }

    private static Dictionary<string, string?> TwoVersions() => new()
    {
        ["Security:Pii:ActiveKeyVersion"] = "2",
        ["Security:Pii:Rewrap:BatchSize"] = "10",
        ["Security:Pii:Rewrap:IdleSeconds"] = "5",
        ["RabbitMq:Port"] = "1"
    };

    private static Task<long> CountAtVersionAsync(NpgsqlConnection admin, int version) =>
        ScalarAsync(admin, $"SELECT count(*) FROM accounts WHERE holder_document_key_version = {version}");

    private static Task<long> AuditCountAsync(NpgsqlConnection admin, string eventType) =>
        ScalarAsync(admin, $"SELECT count(*) FROM audit_log WHERE event_type = '{eventType}'");

    private static Task<long> AuditRewrappedAccountsAsync(NpgsqlConnection admin) =>
        ScalarAsync(
            admin,
            "SELECT coalesce(sum((details ->> 'accounts')::bigint), 0) FROM audit_log WHERE event_type = 'pii.rewrapped'");

    [SuppressMessage("Security", "CA2100",
        Justification = "The statements come from constants and integers of this file.")]
    private static async Task<long> ScalarAsync(NpgsqlConnection admin, string sql)
    {
        await using var command = new NpgsqlCommand(sql, admin);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
