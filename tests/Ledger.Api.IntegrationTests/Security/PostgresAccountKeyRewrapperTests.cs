using System.Text.Json;
using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Accounts;
using Ledger.Application.Security;
using Ledger.Domain.Accounts;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresAccountKeyRewrapperTests(PostgresFixture postgres)
{
    private const string PassId = "0192b7c281aa7e04b1d56f0c2a9e8d33";

    private static readonly int[] KeyVersionTwo = [2];
    private static readonly HolderDocumentProtector VersionOne = PiiKeys.Protector(1, 1);
    private static readonly HolderDocumentProtector VersionTwo = PiiKeys.Protector(2, 1, 2);

    private static readonly Error DocumentFailure =
        new("HOLDER_DOCUMENT_UNREADABLE", "The protected holder document could not be read.", ErrorKind.Unprocessable);

    private static PostgresAccountKeyRewrapper Rewrapper(SecurityDatabase database, RecordingSecurityTelemetry? telemetry = null) =>
        new(database.Connections, Options.Create(database.Settings), telemetry ?? new RecordingSecurityTelemetry());

    private static RewrapBatchRequest Request(
        int activeVersion = 2,
        int batchSize = 500,
        AccountId? after = null,
        string passId = PassId) => new(activeVersion, after, batchSize, passId);

    private static Func<RewrapRow, Result<ProtectedHolderDocument>> Reprotect(HolderDocumentProtector protector) =>
        row => protector.Reprotect(row.Encrypted, row.Id);

    private static async Task<List<(string EventType, string ClientId, string CorrelationId, string Outcome, bool HasAccount, JsonElement Details)>> AuditRowsAsync(
        NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT event_type, client_id, correlation_id, outcome, account_id IS NOT NULL, details::text FROM audit_log ORDER BY id",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var rows = new List<(string, string, string, string, bool, JsonElement)>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            using var details = JsonDocument.Parse(reader.GetString(5));

            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                details.RootElement.Clone()));
        }

        return rows;
    }

    [DockerFact]
    public async Task RewrapBatch_OnlyOlderVersionsAfterTheCursorAreSelectedInIdOrderUpToTheBatchSize()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var older = await AccountSeed.InsertManyAsync(api, VersionOne, 7);
        var current = await AccountSeed.InsertManyAsync(api, VersionTwo, 2, firstDocumentIndex: 100);
        var withoutDocument = AccountId.From(Guid.NewGuid()).Value;
        await AccountSeed.InsertAsync(api, withoutDocument, null);
        var orderedOlder = older.Select(account => account.Id.Value).Order().ToList();
        var rewrapper = Rewrapper(database);

        var first = await rewrapper.RewrapBatchAsync(Request(batchSize: 3), Reprotect(VersionTwo), CancellationToken.None);
        var second = await rewrapper.RewrapBatchAsync(
            Request(batchSize: 3, after: first.LastId),
            Reprotect(VersionTwo),
            CancellationToken.None);
        var third = await rewrapper.RewrapBatchAsync(
            Request(batchSize: 3, after: second.LastId),
            Reprotect(VersionTwo),
            CancellationToken.None);
        var fourth = await rewrapper.RewrapBatchAsync(
            Request(batchSize: 3, after: third.LastId),
            Reprotect(VersionTwo),
            CancellationToken.None);

        first.ShouldBe(new RewrapBatchResult(3, 3, 0, AccountId.From(orderedOlder[2]).Value));
        second.ShouldBe(new RewrapBatchResult(3, 3, 0, AccountId.From(orderedOlder[5]).Value));
        third.ShouldBe(new RewrapBatchResult(1, 1, 0, AccountId.From(orderedOlder[6]).Value));
        fourth.ShouldBe(new RewrapBatchResult(0, 0, 0, null));

        foreach (var account in current)
        {
            (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(2);
        }

        (await AccountSeed.ReadAsync(database, withoutDocument)).KeyVersion.ShouldBeNull();
    }

    [DockerFact]
    public async Task RewrapBatch_RewritesTheThreeColumnsWithTheNewVersionKeepingTheDocumentReadable()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 5);
        var before = new Dictionary<AccountId, StoredDocument>();

        foreach (var account in accounts)
        {
            before[account.Id] = await AccountSeed.ReadAsync(database, account.Id);
        }

        var result = await Rewrapper(database).RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None);

        result.Rewrapped.ShouldBe(5);

        foreach (var account in accounts)
        {
            var after = await AccountSeed.ReadAsync(database, account.Id);

            after.KeyVersion.ShouldBe(2);
            after.Encrypted.ShouldNotBeNull().ShouldNotBe(before[account.Id].Encrypted);
            after.BlindIndex.ShouldNotBeNull().ShouldNotBe(before[account.Id].BlindIndex);
            AesGcmDocumentCipher.ReadKeyVersion(after.Encrypted).Value.ShouldBe((ushort)2);
            VersionTwo.Unprotect(after.Encrypted, account.Id).Value.Normalized.ShouldBe(account.Document.Normalized);
            after.BlindIndex.ShouldBe(VersionTwo.Protect(account.Document, account.Id).BlindIndex.ToArray());
        }
    }

    [DockerFact]
    public async Task SelectRewrapBatchSql_TwoTransactionsSelectingTogether_NeverReceiveTheSameAccount()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await AccountSeed.InsertManyAsync(api, VersionOne, 6);
        await using var firstWorker = await database.OpenAsRoleAsync(PostgresFixture.WorkerRole);
        await using var secondWorker = await database.OpenAsRoleAsync(PostgresFixture.WorkerRole);
        await using var firstTransaction = await firstWorker.BeginTransactionAsync(CancellationToken.None);
        await using var secondTransaction = await secondWorker.BeginTransactionAsync(CancellationToken.None);

        var first = await SelectIdsAsync(firstWorker, firstTransaction, 3);
        var second = await SelectIdsAsync(secondWorker, secondTransaction, 3);

        first.Count.ShouldBe(3);
        second.Count.ShouldBe(3);
        first.Intersect(second).ShouldBeEmpty();
    }

    [DockerFact]
    public async Task RewrapBatch_WhileAWriteHoldsKeyShareOnTheAccount_SelectsAndRewrapsItWithoutWaiting()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var account = (await AccountSeed.InsertManyAsync(api, VersionOne, 1)).Single();
        await using var writer = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await using var writerTransaction = await writer.BeginTransactionAsync(CancellationToken.None);
        await InsertEntryAsync(writer, writerTransaction, account.Id);

        var result = await Rewrapper(database)
            .RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        await writerTransaction.RollbackAsync(CancellationToken.None);

        result.Selected.ShouldBe(1);
        result.Rewrapped.ShouldBe(1);
        (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(2);
    }

    [DockerFact]
    public async Task RewrapBatch_OpenBatch_DoesNotMakeALedgerWriteWaitOrHitTheLockTimeout()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 3);
        var target = accounts[0];
        var inTransform = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();

        Result<ProtectedHolderDocument> Blocking(RewrapRow row)
        {
            inTransform.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(30));

            return VersionTwo.Reprotect(row.Encrypted, row.Id);
        }

        var batch = Rewrapper(database).RewrapBatchAsync(Request(), Blocking, CancellationToken.None);
        await inTransform.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await using var writer = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await using (var lockTimeout = new NpgsqlCommand("SET lock_timeout = '1s'", writer))
        {
            await lockTimeout.ExecuteNonQueryAsync(CancellationToken.None);
        }

        foreach (var account in accounts)
        {
            await using var writerTransaction = await writer.BeginTransactionAsync(CancellationToken.None);
            await InsertEntryAsync(writer, writerTransaction, account.Id);
            await writerTransaction.CommitAsync(CancellationToken.None);
        }

        release.Set();
        var result = await batch.WaitAsync(TimeSpan.FromSeconds(30));

        result.Rewrapped.ShouldBe(3);
        (await SqlRunner.CountAsync(api, "SELECT count(*) FROM ledger_entries")).ShouldBe(3);
        (await AccountSeed.ReadAsync(database, target.Id)).KeyVersion.ShouldBe(2);
    }

    [DockerFact]
    public async Task UpdateRewrappedSql_RowAlreadyAtTheTargetVersion_IsNotRewrittenAgain()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var account = (await AccountSeed.InsertManyAsync(api, VersionTwo, 1)).Single();
        var stale = VersionTwo.Protect(account.Document, account.Id);
        await using var worker = await database.OpenAsRoleAsync(PostgresFixture.WorkerRole);
        await using var command = new NpgsqlCommand(PostgresAccountKeyRewrapper.UpdateRewrappedSql, worker);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid) { Value = new[] { account.Id.Value } });
        command.Parameters.Add(new NpgsqlParameter("encrypted", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea) { Value = new[] { stale.Encrypted.ToArray() } });
        command.Parameters.Add(new NpgsqlParameter("blind_indexes", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea) { Value = new[] { stale.BlindIndex.ToArray() } });
        command.Parameters.Add(new NpgsqlParameter("key_versions", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer) { Value = KeyVersionTwo });

        var affected = await command.ExecuteNonQueryAsync(CancellationToken.None);

        affected.ShouldBe(0);
    }

    [DockerFact]
    public async Task RewrapBatch_RunAgainAfterConverging_FindsNothing()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await AccountSeed.InsertManyAsync(api, VersionOne, 4);
        var rewrapper = Rewrapper(database);

        var first = await rewrapper.RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None);
        var again = await rewrapper.RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None);

        first.Rewrapped.ShouldBe(4);
        again.ShouldBe(new RewrapBatchResult(0, 0, 0, null));
    }

    [DockerFact]
    public async Task RewrapBatch_RecordsTheTwoAuditRowsOfTheContractInTheSameTransaction()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await AccountSeed.InsertManyAsync(api, VersionOne, 3);
        var telemetry = new RecordingSecurityTelemetry();

        await Rewrapper(database, telemetry).RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var rows = await AuditRowsAsync(admin);
        rows.Select(row => row.EventType).ShouldBe(["pii.decrypted", "pii.rewrapped"]);
        rows.ShouldAllBe(row => row.ClientId == "ledger-worker" && row.CorrelationId == PassId
                                                              && row.Outcome == "SUCCESS" && !row.HasAccount);
        rows[0].Details.GetProperty("purpose").GetString().ShouldBe("rewrap");
        rows[0].Details.GetProperty("accounts").GetInt32().ShouldBe(3);
        rows[1].Details.GetProperty("fromVersion").GetInt32().ShouldBe(1);
        rows[1].Details.GetProperty("toVersion").GetInt32().ShouldBe(2);
        rows[1].Details.GetProperty("accounts").GetInt32().ShouldBe(3);
        rows[1].Details.GetProperty("failed").GetInt32().ShouldBe(0);
        telemetry.Recorded.ShouldBe([("pii.decrypted", "SUCCESS"), ("pii.rewrapped", "SUCCESS")]);
    }

    [DockerFact]
    public async Task RewrapBatch_FromVersionIsTheLowestVersionFoundInTheBatch()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var versionThree = PiiKeys.Protector(3, 1, 2, 3);
        await AccountSeed.InsertManyAsync(api, VersionTwo, 2);
        await AccountSeed.InsertManyAsync(api, PiiKeys.Protector(2, 1, 2, 3), 2, firstDocumentIndex: 50);

        await Rewrapper(database).RewrapBatchAsync(Request(activeVersion: 3), Reprotect(versionThree), CancellationToken.None);

        await using var admin = await database.OpenAdministrativeAsync();
        var rewrapped = (await AuditRowsAsync(admin)).Single(row => row.EventType == "pii.rewrapped");
        rewrapped.Details.GetProperty("fromVersion").GetInt32().ShouldBe(2);
        rewrapped.Details.GetProperty("toVersion").GetInt32().ShouldBe(3);
    }

    [DockerFact]
    public async Task RewrapBatch_TransformThatFailsForSomeRows_LeavesThemUntouchedAndCountsThem()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 5);
        var refused = accounts.Take(2).Select(account => account.Id).ToHashSet();
        var untouched = new Dictionary<AccountId, StoredDocument>();

        foreach (var id in refused)
        {
            untouched[id] = await AccountSeed.ReadAsync(database, id);
        }

        var result = await Rewrapper(database).RewrapBatchAsync(
            Request(),
            row => refused.Contains(row.Id)
                ? Result.Failure<ProtectedHolderDocument>(DocumentFailure)
                : VersionTwo.Reprotect(row.Encrypted, row.Id),
            CancellationToken.None);

        result.Selected.ShouldBe(5);
        result.Rewrapped.ShouldBe(3);
        result.Failed.ShouldBe(2);

        foreach (var id in refused)
        {
            var after = await AccountSeed.ReadAsync(database, id);

            after.KeyVersion.ShouldBe(1);
            after.Encrypted.ShouldBe(untouched[id].Encrypted);
            after.BlindIndex.ShouldBe(untouched[id].BlindIndex);
        }

        await using var admin = await database.OpenAdministrativeAsync();
        var rows = await AuditRowsAsync(admin);
        rows.Single(row => row.EventType == "pii.decrypted").Details.GetProperty("accounts").GetInt32().ShouldBe(3);
        rows.Single(row => row.EventType == "pii.rewrapped").Details.GetProperty("failed").GetInt32().ShouldBe(2);
    }

    [DockerFact]
    public async Task RewrapBatch_EveryRowFails_RecordsOnlyTheRewrappedRowAndNothingDecrypted()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await AccountSeed.InsertManyAsync(api, VersionOne, 3);

        var result = await Rewrapper(database).RewrapBatchAsync(
            Request(),
            _ => Result.Failure<ProtectedHolderDocument>(DocumentFailure),
            CancellationToken.None);

        result.Selected.ShouldBe(3);
        result.Rewrapped.ShouldBe(0);
        result.Failed.ShouldBe(3);
        result.LastId.ShouldNotBeNull();
        await using var admin = await database.OpenAdministrativeAsync();
        var rows = await AuditRowsAsync(admin);
        rows.Select(row => row.EventType).ShouldBe(["pii.rewrapped"]);
        rows[0].Details.GetProperty("accounts").GetInt32().ShouldBe(0);
        rows[0].Details.GetProperty("failed").GetInt32().ShouldBe(3);
    }

    [DockerFact]
    public async Task RewrapBatch_FailureBeforeTheCommit_UndoesTheUpdatesAndTheAuditRows()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 4);
        var tooLongPassId = new string('p', 100);

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            Rewrapper(database).RewrapBatchAsync(
                Request(passId: tooLongPassId),
                Reprotect(VersionTwo),
                CancellationToken.None));

        failure.SqlState.ShouldBe(PostgresErrorCodes.StringDataRightTruncation);

        foreach (var account in accounts)
        {
            (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(1);
        }

        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(0);
    }

    [DockerFact]
    public async Task RewrapBatch_TransformThatThrows_RollsEverythingBack()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 4);
        var calls = 0;

        await Should.ThrowAsync<KeyProviderUnavailableException>(() =>
            Rewrapper(database).RewrapBatchAsync(
                Request(),
                row => ++calls == 3 ? throw new KeyProviderUnavailableException() : VersionTwo.Reprotect(row.Encrypted, row.Id),
                CancellationToken.None));

        foreach (var account in accounts)
        {
            (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(1);
        }

        await using var admin = await database.OpenAdministrativeAsync();
        (await SqlRunner.CountAsync(admin, "SELECT count(*) FROM audit_log")).ShouldBe(0);
    }

    [DockerFact]
    public async Task RewrapBatch_TouchesOnlyAccountsAndAuditLog()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 3);

        foreach (var account in accounts)
        {
            await AccountSeed.InsertBalanceAsync(api, account.Id);
            await using var writerTransaction = await api.BeginTransactionAsync(CancellationToken.None);
            await InsertEntryAsync(api, writerTransaction, account.Id);
            await writerTransaction.CommitAsync(CancellationToken.None);
        }

        await using var admin = await database.OpenAdministrativeAsync();
        var before = await FingerprintAsync(admin);

        await Rewrapper(database).RewrapBatchAsync(Request(), Reprotect(VersionTwo), CancellationToken.None);

        (await FingerprintAsync(admin)).ShouldBe(before);
    }

    [DockerFact]
    public async Task SelectRewrapBatchSql_WithTheApiRole_IsDeniedBecauseOnlyTheWorkerMayUpdateAccounts()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        await AccountSeed.InsertManyAsync(api, VersionOne, 1);
        await using var select = new NpgsqlCommand(PostgresAccountKeyRewrapper.SelectRewrapBatchSql, api);
        select.Parameters.AddWithValue("active_version", 2);
        select.Parameters.AddWithValue("after_id", Guid.Empty);
        select.Parameters.AddWithValue("batch_size", 10);

        var denied = await Should.ThrowAsync<PostgresException>(() => select.ExecuteNonQueryAsync(CancellationToken.None));

        denied.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task<List<Guid>> SelectIdsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int batchSize)
    {
        await using var command = new NpgsqlCommand(PostgresAccountKeyRewrapper.SelectRewrapBatchSql, connection, transaction);
        command.Parameters.AddWithValue("active_version", 2);
        command.Parameters.AddWithValue("after_id", Guid.Empty);
        command.Parameters.AddWithValue("batch_size", batchSize);

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var ids = new List<Guid>();

        while (await reader.ReadAsync(CancellationToken.None))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static async Task InsertEntryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AccountId accountId)
    {
        var entryId = Guid.NewGuid();

        await using (var key = new NpgsqlCommand(
                         """
                         INSERT INTO idempotency_keys (account_id, idempotency_key, request_hash, hash_version, entry_id)
                         VALUES (@account_id, @key, @hash, 1, @entry_id)
                         """,
                         connection,
                         transaction))
        {
            key.Parameters.AddWithValue("account_id", accountId.Value);
            key.Parameters.AddWithValue("key", $"key-{entryId:N}");
            key.Parameters.AddWithValue("hash", new byte[32]);
            key.Parameters.AddWithValue("entry_id", entryId);

            await key.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await using var entry = new NpgsqlCommand(
            """
            INSERT INTO ledger_entries (id, account_id, account_version, type, amount, currency, balance_after,
                                        recorded_at, occurred_at, client_id, correlation_id)
            VALUES (@id, @account_id, 1, 'CREDIT', 10.00, 'BRL', 10.00, clock_timestamp(), clock_timestamp(), 'tests', 'corr')
            """,
            connection,
            transaction);
        entry.Parameters.AddWithValue("id", entryId);
        entry.Parameters.AddWithValue("account_id", accountId.Value);

        await entry.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<string> FingerprintAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
              (SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t.id)), '') FROM ledger_entries AS t) || '|' ||
              (SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t.account_id)), '') FROM account_balances AS t) || '|' ||
              (SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t.id)), '') FROM outbox_messages AS t) || '|' ||
              (SELECT coalesce(md5(string_agg(t::text, ',' ORDER BY t.account_id, t.idempotency_key)), '') FROM idempotency_keys AS t)
            """,
            connection);

        return Convert.ToString(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture)
               ?? string.Empty;
    }
}
