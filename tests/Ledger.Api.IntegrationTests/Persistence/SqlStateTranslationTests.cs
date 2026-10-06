using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class SqlStateTranslationTests(PostgresFixture postgres)
{
    private const string RedactedDetail =
        "Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information.";

    [DockerFact]
    public void TheConnectionsOfTheTests_UseTheProductionSettingForErrorDetail()
    {
        postgres.Settings.IncludeErrorDetail.ShouldBeFalse();
        new NpgsqlConnectionStringBuilder(PostgresConnectionString.Build(postgres.Settings, PostgresSource.Write))
            .IncludeErrorDetail.ShouldBeFalse();
    }

    [DockerFact]
    public async Task AUniqueViolation_WithoutErrorDetail_StillCarriesTheConstraintNameAndRedactsTheRowDetail()
    {
        await using var connection = await postgres.ConnectionFactory.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
        var id = Guid.CreateVersion7();

        await InsertAccountAsync(connection, id);

        var violation = await Should.ThrowAsync<PostgresException>(() => InsertAccountAsync(connection, id));

        violation.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        violation.ConstraintName.ShouldBe("pk_accounts");
        violation.Detail.ShouldBe(RedactedDetail);
        violation.Detail.ShouldNotBeNull().ShouldNotContain(id.ToString());
    }

    [DockerFact]
    public async Task AForeignKeyViolation_WithoutErrorDetail_StillCarriesTheConstraintName()
    {
        await using var connection = await postgres.ConnectionFactory.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
        await using var command = new NpgsqlCommand(
            "INSERT INTO idempotency_keys (account_id, idempotency_key, request_hash, entry_id) VALUES (gen_random_uuid(), 'k', decode(repeat('00', 32), 'hex'), gen_random_uuid())",
            connection);

        var violation = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        violation.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        violation.ConstraintName.ShouldBe("fk_idempotency_keys_account_id");
        violation.Detail.ShouldBe(RedactedDetail);
    }

    [DockerFact]
    public async Task TheReversalRace_IsTranslatedToAlreadyReversedWithoutErrorDetail()
    {
        await using var host = LedgerHost.Create(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        var debit = await host.RegisterAsync(accountId, "to-reverse", EntryType.Debit, 30.00m);
        await host.ReverseAsync(accountId, debit.Value.Entry.Id, "first-reversal");

        var second = await host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(
                SecondReversal(accountId, debit.Value.Entry.Id),
                token));

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(EntryErrors.AlreadyReversed);
    }

    [DockerFact]
    public async Task AReservationOnAnUnknownAccount_IsTranslatedToAccountNotFoundWithoutErrorDetail()
    {
        await using var host = LedgerHost.Create(postgres);
        var unknown = AccountId.From(Guid.CreateVersion7()).Value;

        var outcome = await host.RegisterAsync(unknown, "ghost-account", EntryType.Credit, 1.00m);

        outcome.IsFailure.ShouldBeTrue();
        outcome.Error.ShouldBe(AccountErrors.NotFound);
    }

    private static Ledger.Application.Entries.NewEntry SecondReversal(AccountId accountId, EntryId originalId)
    {
        var plan = new ReversalCandidate(
            originalId,
            EntryType.Debit,
            Ledger.Domain.Shared.Money.CreatePositive(30.00m, LedgerHost.Currency).Value,
            null,
            null).Plan().Value;
        var entry = Entry.ReversalOf(plan, EntryId.From(Guid.CreateVersion7()).Value, accountId, null).Value;

        return new Ledger.Application.Entries.NewEntry(entry, LedgerHost.ClientId, LedgerHost.CorrelationId);
    }

    private static async Task InsertAccountAsync(NpgsqlConnection connection, Guid id)
    {
        await using var command = new NpgsqlCommand("INSERT INTO accounts (id, currency) VALUES (@id, 'BRL')", connection);

        command.Parameters.AddWithValue("id", id);

        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
