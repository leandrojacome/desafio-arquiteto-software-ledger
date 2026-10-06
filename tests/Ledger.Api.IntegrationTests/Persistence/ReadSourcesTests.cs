using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Application.Abstractions;
using Ledger.Domain.Entries;
using Ledger.Infrastructure.Persistence;
using Npgsql;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class ReadSourcesTests(PostgresFixture postgres)
{
    public static TheoryData<PostgresSource, string> ReadSources => new()
    {
        { PostgresSource.Balance, "ledger-api-balance" },
        { PostgresSource.Statement, "ledger-api-statement" }
    };

    [DockerTheory]
    [MemberData(nameof(ReadSources))]
    public async Task AReadSource_OpensReadOnlySessionsNamedAfterItself(PostgresSource source, string applicationName)
    {
        await using var host = LedgerHost.Create(postgres);
        await using var connection = await host.Connections.OpenConnectionAsync(source, CancellationToken.None);

        (await SettingAsync(connection, "default_transaction_read_only")).ShouldBe("on");
        (await SettingAsync(connection, "application_name")).ShouldBe(applicationName);
        (await SettingAsync(connection, "statement_timeout")).ShouldBe("1500ms");
    }

    [DockerTheory]
    [MemberData(nameof(ReadSources))]
    public async Task AReadSource_RefusesAnyWrite(PostgresSource source, string applicationName)
    {
        await using var host = LedgerHost.Create(postgres);
        await using var connection = await host.Connections.OpenConnectionAsync(source, CancellationToken.None);
        await using var command = new NpgsqlCommand("INSERT INTO accounts (id, currency) VALUES (gen_random_uuid(), 'BRL')", connection);

        var refused = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        applicationName.ShouldNotBeEmpty();
        refused.SqlState.ShouldBe(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    [DockerFact]
    public async Task TheWriteSource_IsNotReadOnly()
    {
        await using var host = LedgerHost.Create(postgres);
        await using var connection = await host.Connections.OpenConnectionAsync(PostgresSource.Write, CancellationToken.None);

        (await SettingAsync(connection, "default_transaction_read_only")).ShouldBe("off");
        (await SettingAsync(connection, "application_name")).ShouldBe("ledger-api-write");
    }

    [DockerFact]
    public async Task EachReadCommand_IsMeasuredUnderItsOwnOperation()
    {
        await using var host = LedgerHost.Create(postgres);
        var accountId = await host.CreateFundedAccountAsync(100.00m);
        using var capture = new MeterCapture("ledger.db.command.duration");

        await host.Service<IBalanceReader>().ReadCurrentAsync(accountId, CancellationToken.None);
        await host.Service<IBalanceReader>().ReadAtAsync(accountId, host.Time.GetUtcNow().AddMinutes(-1), CancellationToken.None);
        await host.Service<IStatementReader>().ReadPageAsync(accountId, StatementBounds.Resolve(null, null, null), 10, CancellationToken.None);
        await host.Service<IStatementReader>().AccountExistsAsync(accountId, CancellationToken.None);

        capture.Measurements.Select(measurement => measurement.Tags["operation"]?.ToString())
            .ShouldBe(["select_balance", "select_balance_as_of", "select_entries"]);
        capture.Measurements.ShouldAllBe(measurement => measurement.Value >= 0);
    }

    private static async Task<string> SettingAsync(NpgsqlConnection connection, string name)
    {
        await using var command = new NpgsqlCommand("SELECT current_setting(@name)", connection);

        command.Parameters.AddWithValue("name", name);

        return (string)(await command.ExecuteScalarAsync() ?? string.Empty);
    }
}
