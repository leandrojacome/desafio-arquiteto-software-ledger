using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Application.Accounts;
using Ledger.Domain.Accounts;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Security;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class RewrapConvergenceTests(PostgresFixture postgres)
{
    private const string FindByBlindIndexSql = """
                                               SELECT id
                                               FROM accounts
                                               WHERE holder_document_blind_index = ANY (@candidates)
                                               """;

    private static readonly HolderDocumentProtector VersionOne = PiiKeys.Protector(1, 1);
    private static readonly HolderDocumentProtector VersionTwo = PiiKeys.Protector(2, 1, 2);

    private static RewrapAccountsHandler Handler(SecurityDatabase database, int activeVersion, params int[] versions)
    {
        var provider = PiiKeys.Provider(activeVersion, versions);
        var protector = new HolderDocumentProtector(provider, new AesGcmDocumentCipher());
        var rewrapper = new PostgresAccountKeyRewrapper(
            database.Connections,
            Options.Create(database.Settings),
            new RecordingSecurityTelemetry());

        return new RewrapAccountsHandler(
            rewrapper,
            protector,
            provider,
            new RecordingSecurityTelemetry(),
            NullLogger<RewrapAccountsHandler>.Instance);
    }

    private static RewrapAccountsCommand Command(int batchSize = 50, int activeVersion = 2) =>
        new(activeVersion, batchSize, Guid.NewGuid().ToString("N"));

    private static async Task<Guid?> FindAsync(
        SecurityDatabase database,
        HolderDocumentProtector protector,
        HolderDocument document)
    {
        await using var command = new NpgsqlCommand(FindByBlindIndexSql, await database.WorkerReaderAsync());
        command.Parameters.Add(new NpgsqlParameter("candidates", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Bytea)
        {
            Value = protector.BlindIndexCandidates(document).Select(candidate => candidate.ToArray()).ToArray()
        });

        var found = await command.ExecuteScalarAsync(CancellationToken.None);

        return found as Guid?;
    }

    [DockerFact]
    public async Task Handle_RewrapsEveryAccountAndTheNextPassDeclaresTheConvergence()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 120);
        var handler = Handler(database, 2, 1, 2);

        var first = await handler.HandleAsync(Command(), CancellationToken.None);
        var second = await handler.HandleAsync(Command(), CancellationToken.None);

        first.Rewrapped.ShouldBe(120);
        first.Failed.ShouldBe(0);
        first.Converged.ShouldBeFalse();
        second.Rewrapped.ShouldBe(0);
        second.Converged.ShouldBeTrue();

        foreach (var account in accounts)
        {
            var stored = await AccountSeed.ReadAsync(database, account.Id);

            stored.KeyVersion.ShouldBe(2);
            VersionTwo.Unprotect(stored.Encrypted.ShouldNotBeNull(), account.Id).Value.Normalized
                .ShouldBe(account.Document.Normalized);
        }
    }

    [DockerFact]
    public async Task BlindIndexLookup_FindsTheAccountBeforeDuringAndAfterTheRewrap()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 30);
        var window = PiiKeys.Protector(2, 1, 2);

        foreach (var account in accounts)
        {
            (await FindAsync(database, window, account.Document)).ShouldBe(account.Id.Value);
        }

        await Handler(database, 2, 1, 2).HandleAsync(Command(batchSize: 10), CancellationToken.None);

        foreach (var account in accounts)
        {
            (await FindAsync(database, window, account.Document)).ShouldBe(account.Id.Value);
            (await FindAsync(database, VersionTwo, account.Document)).ShouldBe(account.Id.Value);
        }
    }

    [DockerFact]
    public async Task BlindIndexLookup_DuringTheWindow_FindsAccountsOfBothVersions()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 20);
        var newer = await AccountSeed.InsertManyAsync(api, VersionTwo, 20, firstDocumentIndex: 500);
        var window = PiiKeys.Protector(2, 1, 2);

        foreach (var account in accounts.Concat(newer))
        {
            (await FindAsync(database, window, account.Document)).ShouldBe(account.Id.Value);
        }

        foreach (var account in accounts)
        {
            (await FindAsync(database, VersionTwo, account.Document)).ShouldNotBeNull();
            (await FindAsync(database, PiiKeys.Protector(2, 1, 2), account.Document)).ShouldNotBeNull();
        }
    }

    [DockerFact]
    public async Task Handle_TamperedBlob_IsSkippedKeepsThePassUnconvergedAndNeverLoopsForever()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 5);
        var broken = accounts[2];
        var original = (await AccountSeed.ReadAsync(database, broken.Id)).Encrypted.ShouldNotBeNull();
        var tampered = (byte[])original.Clone();
        tampered[20] ^= 0x01;

        await using var admin = await database.OpenAdministrativeAsync();
        await SetBlobAsync(admin, broken.Id, tampered);
        var handler = Handler(database, 2, 1, 2);

        var first = await handler.HandleAsync(Command(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        var second = await handler.HandleAsync(Command(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        first.Rewrapped.ShouldBe(4);
        first.Failed.ShouldBe(1);
        first.Converged.ShouldBeFalse();
        second.Rewrapped.ShouldBe(0);
        second.Failed.ShouldBe(1);
        second.Converged.ShouldBeFalse();
        (await AccountSeed.ReadAsync(database, broken.Id)).KeyVersion.ShouldBe(1);

        await SetBlobAsync(admin, broken.Id, original);

        var third = await handler.HandleAsync(Command(), CancellationToken.None);
        var fourth = await handler.HandleAsync(Command(), CancellationToken.None);

        third.Rewrapped.ShouldBe(1);
        third.Failed.ShouldBe(0);
        fourth.Converged.ShouldBeTrue();
        (await AccountSeed.ReadAsync(database, broken.Id)).KeyVersion.ShouldBe(2);
    }

    [DockerFact]
    public async Task Handle_AccountWhoseKeyVersionWasLost_IsSkippedAndTheRestIsRewrapped()
    {
        await using var database = await SecurityDatabase.CreateAsync(postgres);
        await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
        var lost = await AccountSeed.InsertManyAsync(api, VersionOne, 3);
        var readable = await AccountSeed.InsertManyAsync(api, VersionTwo, 2, firstDocumentIndex: 900);
        var handler = Handler(database, 3, 2, 3);

        var result = await handler.HandleAsync(Command(activeVersion: 3), CancellationToken.None);

        result.Rewrapped.ShouldBe(2);
        result.Failed.ShouldBe(3);
        result.Converged.ShouldBeFalse();

        foreach (var account in lost)
        {
            (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(1);
        }

        foreach (var account in readable)
        {
            (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(3);
        }
    }

    [DockerFact]
    [Trait("Category", "Concurrency")]
    public async Task Handle_TwoWorkersAtTheSameTime_NeverRewrapTheSameAccountTwiceAndEndWhereOneWorkerWould()
    {
        await ConcurrencySettings.RepeatAsync(async () =>
        {
            await using var database = await SecurityDatabase.CreateAsync(postgres);
            await using var api = await database.OpenAsRoleAsync(PostgresFixture.ApiRole);
            var accounts = await AccountSeed.InsertManyAsync(api, VersionOne, 300);
            var first = Handler(database, 2, 1, 2);
            var second = Handler(database, 2, 1, 2);

            var passes = await ParallelGate.RunAsync(
                2,
                index => (index == 0 ? first : second).HandleAsync(Command(batchSize: 20), CancellationToken.None));

            var final = await first.HandleAsync(Command(batchSize: 20), CancellationToken.None);

            (passes[0].Rewrapped + passes[1].Rewrapped + final.Rewrapped).ShouldBe(300);
            passes.Sum(pass => pass.Failed).ShouldBe(0);

            foreach (var account in accounts)
            {
                (await AccountSeed.ReadAsync(database, account.Id)).KeyVersion.ShouldBe(2);
            }

            await using var admin = await database.OpenAdministrativeAsync();
            var rewrappedAccounts = await SumRewrappedAsync(admin);
            rewrappedAccounts.ShouldBe(300);
        });
    }

    private static async Task SetBlobAsync(NpgsqlConnection admin, AccountId id, byte[] blob)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE accounts SET holder_document_encrypted = @blob WHERE id = @id",
            admin);
        command.Parameters.AddWithValue("blob", blob);
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<long> SumRewrappedAsync(NpgsqlConnection admin)
    {
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(sum((details ->> 'accounts')::bigint), 0) FROM audit_log WHERE event_type = 'pii.rewrapped'",
            admin);

        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None), System.Globalization.CultureInfo.InvariantCulture);
    }
}
