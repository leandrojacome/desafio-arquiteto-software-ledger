using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Entries;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;
using Ledger.Domain.Shared;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class PostgresEntryRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private LedgerHost _host = null!;
    private LedgerQueries _queries = null!;

    public Task InitializeAsync()
    {
        _host = LedgerHost.Create(postgres);
        _queries = new LedgerQueries(postgres);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
    }

    [DockerFact]
    public async Task TryApplyAsync_CreditOnANewAccount_GivesPositionOneAndTheCreditedBalance()
    {
        var accountId = await _host.CreateAccountAsync();

        await using var session = await Session.OpenAsync(_host);
        var applied = await session.Entries.TryApplyAsync(Credit(accountId, 100.00m), CancellationToken.None);

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Entry.AccountVersion.ShouldBe(1);
        applied.Value.Entry.BalanceAfter.Amount.ShouldBe(100.00m);
        applied.Value.Entry.Type.ShouldBe(EntryType.Credit);
        applied.Value.RecordedAtCorrected.ShouldBeFalse();
    }

    [DockerFact]
    public async Task TryApplyAsync_RecordedAt_IsLaterThanTheCreationOfTheAccount()
    {
        var accountId = await _host.CreateAccountAsync();
        var createdAt = (await _queries.BalanceRowAsync(accountId)).LastRecordedAt;

        await using var session = await Session.OpenAsync(_host);
        var applied = await session.Entries.TryApplyAsync(Credit(accountId, 1.00m), CancellationToken.None);

        applied.Value.Entry.RecordedAt.UtcDateTime.ShouldBeGreaterThan(createdAt);
    }

    [DockerFact]
    public async Task TryApplyAsync_DebitAfterACredit_GivesPositionTwoAndTheReducedBalance()
    {
        var accountId = await _host.CreateAccountAsync();

        await using var session = await Session.OpenAsync(_host);
        await session.Entries.TryApplyAsync(Credit(accountId, 100.00m), CancellationToken.None);
        var debit = await session.Entries.TryApplyAsync(Debit(accountId, 30.00m), CancellationToken.None);

        debit.Value.Entry.AccountVersion.ShouldBe(2);
        debit.Value.Entry.BalanceAfter.Amount.ShouldBe(70.00m);
        debit.Value.Entry.Amount.Amount.ShouldBe(30.00m);
    }

    [DockerFact]
    public async Task TryApplyAsync_WithoutOccurredAt_UsesTheRecordedInstant()
    {
        var accountId = await _host.CreateAccountAsync();

        await using var session = await Session.OpenAsync(_host);
        var applied = await session.Entries.TryApplyAsync(Credit(accountId, 5.00m), CancellationToken.None);

        applied.Value.Entry.OccurredAt.ShouldBe(applied.Value.Entry.RecordedAt);
    }

    [DockerFact]
    public async Task TryApplyAsync_WithAnOccurredAtInAnotherOffset_StoresItInUtc()
    {
        var accountId = await _host.CreateAccountAsync();
        var occurredAt = new DateTimeOffset(2026, 10, 1, 11, 3, 10, TimeSpan.FromHours(-3));

        await using var session = await Session.OpenAsync(_host);
        var applied = await session.Entries.TryApplyAsync(
            Credit(accountId, 5.00m, occurredAt: occurredAt),
            CancellationToken.None);

        applied.Value.Entry.OccurredAt.ShouldBe(occurredAt);
        applied.Value.Entry.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        applied.Value.Entry.OccurredAt.UtcDateTime.ShouldBe(new DateTime(2026, 10, 1, 14, 3, 10, DateTimeKind.Utc));
    }

    [DockerFact]
    public async Task TryApplyAsync_StoresTheFreeTextAndTheCallerIdentity()
    {
        var accountId = await _host.CreateAccountAsync();
        var entry = Credit(accountId, 5.00m, description: "Salário", reference: "ref-123", clientId: "pix-core");

        await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(entry, token));

        var stored = (await _queries.EntriesAsync(accountId)).Single();

        stored.Description.ShouldBe("Salário");
        stored.Reference.ShouldBe("ref-123");
        stored.ClientId.ShouldBe("pix-core");
        stored.CorrelationId.ShouldBe(LedgerHost.CorrelationId);
        stored.Type.ShouldBe("CREDIT");
        stored.Currency.ShouldBe("BRL");
    }

    [DockerFact]
    public async Task TryApplyAsync_WithoutOptionalFields_StoresNulls()
    {
        var accountId = await _host.CreateAccountAsync();

        await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Credit(accountId, 5.00m), token));

        var stored = (await _queries.EntriesAsync(accountId)).Single();

        stored.Description.ShouldBeNull();
        stored.Reference.ShouldBeNull();
        stored.ReversesEntryId.ShouldBeNull();
    }

    [DockerFact]
    public async Task TryApplyAsync_ReturnsTheMicrosecondPrecisionOfTheStoredRecordedAt()
    {
        var accountId = await _host.CreateAccountAsync();

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Credit(accountId, 5.00m), token));

        var stored = (await _queries.EntriesAsync(accountId)).Single();

        applied.Value.Entry.RecordedAt.UtcDateTime.ShouldBe(stored.RecordedAt);
        (applied.Value.Entry.RecordedAt.UtcTicks % 10).ShouldBe(0);
    }

    [DockerFact]
    public async Task TryApplyAsync_KeepsTheBalanceRowInStepWithTheEntry()
    {
        var accountId = await _host.CreateAccountAsync();

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Credit(accountId, 42.00m), token));

        var row = await _queries.BalanceRowAsync(accountId);

        row.Balance.ShouldBe(42.00m);
        row.Version.ShouldBe(1);
        row.LastEntryId.ShouldBe(applied.Value.Entry.Id.Value);
        row.LastRecordedAt.ShouldBe(applied.Value.Entry.RecordedAt.UtcDateTime);
    }

    [DockerFact]
    public async Task TryApplyAsync_DebitAboveTheBalance_IsNotMatchedAndChangesNothing()
    {
        var accountId = await _host.CreateFundedAccountAsync(10.00m);

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Debit(accountId, 10.01m), token));

        var row = await _queries.BalanceRowAsync(accountId);

        applied.IsFailure.ShouldBeTrue();
        applied.Error.ShouldBe(ApplyErrors.NotMatched);
        row.Balance.ShouldBe(10.00m);
        row.Version.ShouldBe(1);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(1);
    }

    [DockerFact]
    public async Task TryApplyAsync_DebitExactlyToTheOverdraftLimit_IsAccepted()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m, overdraftLimit: 500.00m);

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Debit(accountId, 600.00m), token));

        applied.IsSuccess.ShouldBeTrue();
        applied.Value.Entry.BalanceAfter.Amount.ShouldBe(-500.00m);
    }

    [DockerFact]
    public async Task TryApplyAsync_OneCentBeyondTheOverdraftLimit_IsNotMatched()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m, overdraftLimit: 500.00m);

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(Debit(accountId, 600.01m), token));

        applied.Error.ShouldBe(ApplyErrors.NotMatched);
    }

    [DockerFact]
    public async Task TryApplyAsync_WithAnotherCurrency_IsNotMatchedAndChangesNothing()
    {
        var accountId = await _host.CreateFundedAccountAsync(50.00m);
        var otherCurrencyEntry = NewEntryOf(accountId, EntryType.Credit, 5.00m, "EUR");

        var applied = await _host.ExecuteAsync((scope, token) => scope.Entries.TryApplyAsync(otherCurrencyEntry, token));

        applied.Error.ShouldBe(ApplyErrors.NotMatched);
        (await _queries.BalanceRowAsync(accountId)).Balance.ShouldBe(50.00m);
    }

    [DockerFact]
    public async Task TryApplyAsync_ForAMissingAccount_IsNotMatched()
    {
        var missing = AccountId.From(Guid.CreateVersion7()).Value;

        var applied = await _host.ExecuteAsync((scope, token) => scope.Entries.TryApplyAsync(Credit(missing, 1.00m), token));

        applied.Error.ShouldBe(ApplyErrors.NotMatched);
    }

    [DockerFact]
    public async Task FindForReversalAsync_ReturnsTheOriginalAsItIsStored()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m);
        var original = (await _queries.EntriesAsync(accountId)).Single();

        var candidate = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(
                await scope.Entries.FindForReversalAsync(accountId, EntryId.From(original.Id).Value, token)));

        candidate.Candidate.ShouldNotBeNull();
        candidate.Candidate.Id.Value.ShouldBe(original.Id);
        candidate.Candidate.Type.ShouldBe(EntryType.Credit);
        candidate.Candidate.Amount.Amount.ShouldBe(100.00m);
        candidate.Candidate.Amount.Currency.ShouldBe("BRL");
        candidate.Candidate.ReversesEntryId.ShouldBeNull();
        candidate.Candidate.ReversalId.ShouldBeNull();
    }

    [DockerFact]
    public async Task FindForReversalAsync_ForAnEntryOfAnotherAccount_ReturnsNull()
    {
        var owner = await _host.CreateFundedAccountAsync(100.00m);
        var other = await _host.CreateAccountAsync();
        var original = (await _queries.EntriesAsync(owner)).Single();

        var found = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(
                await scope.Entries.FindForReversalAsync(other, EntryId.From(original.Id).Value, token)));

        found.Candidate.ShouldBeNull();
    }

    [DockerFact]
    public async Task FindForReversalAsync_ForAnUnknownEntry_ReturnsNull()
    {
        var accountId = await _host.CreateAccountAsync();

        var found = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(
                await scope.Entries.FindForReversalAsync(accountId, EntryId.From(Guid.CreateVersion7()).Value, token)));

        found.Candidate.ShouldBeNull();
    }

    [DockerFact]
    public async Task FindForReversalAsync_AfterAReversal_CarriesTheReversalId()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m);
        var debit = await _host.RegisterAsync(accountId, "to-reverse", EntryType.Debit, 30.00m);
        var reversal = await _host.ReverseAsync(accountId, debit.Value.Entry.Id, "reversal-1");

        var found = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(
                await scope.Entries.FindForReversalAsync(accountId, debit.Value.Entry.Id, token)));

        reversal.IsSuccess.ShouldBeTrue();
        found.Candidate.ShouldNotBeNull();
        found.Candidate.ReversalId.ShouldBe(reversal.Value.Entry.Id);

        var reversalCandidate = await _host.InUnitOfWorkAsync(
            async (scope, token) => new Found(
                await scope.Entries.FindForReversalAsync(accountId, reversal.Value.Entry.Id, token)));

        reversalCandidate.Candidate.ShouldNotBeNull();
        reversalCandidate.Candidate.ReversesEntryId.ShouldBe(debit.Value.Entry.Id);
    }

    [DockerFact]
    public async Task TryApplyAsync_SecondReversalOfTheSameEntry_IsAlreadyReversedAndLeavesTheBalanceAlone()
    {
        var accountId = await _host.CreateFundedAccountAsync(100.00m);
        var debit = await _host.RegisterAsync(accountId, "to-reverse", EntryType.Debit, 30.00m);
        await _host.ReverseAsync(accountId, debit.Value.Entry.Id, "reversal-1");
        var balanceAfterTheFirst = (await _queries.BalanceRowAsync(accountId)).Balance;

        var secondReversal = ReversalOf(accountId, debit.Value.Entry.Id, 30.00m);

        var applied = await _host.ExecuteAsync(
            (scope, token) => scope.Entries.TryApplyAsync(secondReversal, token));

        applied.IsFailure.ShouldBeTrue();
        applied.Error.ShouldBe(EntryErrors.AlreadyReversed);
        (await _queries.BalanceRowAsync(accountId)).Balance.ShouldBe(balanceAfterTheFirst);
        (await _queries.CountEntriesAsync(accountId)).ShouldBe(3);
    }

    private static NewEntry Credit(
        AccountId accountId,
        decimal amount,
        DateTimeOffset? occurredAt = null,
        string? description = null,
        string? reference = null,
        string clientId = LedgerHost.ClientId) =>
        NewEntryOf(accountId, EntryType.Credit, amount, LedgerHost.Currency, occurredAt, description, reference, clientId);

    private static NewEntry Debit(AccountId accountId, decimal amount) =>
        NewEntryOf(accountId, EntryType.Debit, amount, LedgerHost.Currency);

    private static NewEntry NewEntryOf(
        AccountId accountId,
        EntryType type,
        decimal amount,
        string currency,
        DateTimeOffset? occurredAt = null,
        string? description = null,
        string? reference = null,
        string clientId = LedgerHost.ClientId)
    {
        var id = EntryId.From(Guid.CreateVersion7()).Value;
        var money = Money.CreatePositive(amount, currency).Value;

        var entry = type == EntryType.Credit
            ? Entry.Credit(id, accountId, money, occurredAt, description, reference)
            : Entry.Debit(id, accountId, money, occurredAt, description, reference);

        return new NewEntry(entry.Value, clientId, LedgerHost.CorrelationId);
    }

    private static NewEntry ReversalOf(AccountId accountId, EntryId originalId, decimal amount)
    {
        var plan = new ReversalCandidate(
            originalId,
            EntryType.Debit,
            Money.CreatePositive(amount, LedgerHost.Currency).Value,
            null,
            null).Plan().Value;
        var entry = Entry.ReversalOf(plan, EntryId.From(Guid.CreateVersion7()).Value, accountId, null);

        return new NewEntry(entry.Value, LedgerHost.ClientId, LedgerHost.CorrelationId);
    }

    private sealed record Found(ReversalCandidate? Candidate);

    private sealed class Session : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;

        private Session(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
            Entries = new PostgresEntryRepository(connection, transaction);
        }

        public PostgresEntryRepository Entries { get; }

        public static async Task<Session> OpenAsync(LedgerHost host)
        {
            var connection = await host.Connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);
            var transaction = await connection.BeginTransactionAsync(CancellationToken.None);

            return new Session(connection, transaction);
        }

        public async ValueTask DisposeAsync()
        {
            await _transaction.RollbackAsync(CancellationToken.None);
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
