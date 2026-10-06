using Ledger.Api.IntegrationTests.Infrastructure;
using Ledger.Api.IntegrationTests.Persistence.Support;
using Ledger.Api.IntegrationTests.Writes.Support;
using Ledger.Domain.Entries;
using Npgsql;
using NpgsqlTypes;

namespace Ledger.Api.IntegrationTests.Persistence;

[Collection(PostgresCollectionDefinition.Name)]
[Trait("Category", "Integration")]
public sealed class NpgsqlInfinityConversionTests(PostgresFixture postgres)
{
    [DockerFact]
    public async Task TheExtremesOfDateTime_AreWrittenAsInstantsAndNeverAsInfinity()
    {
        await using var command = postgres.AdministrativeSource.CreateCommand(
            """
            SELECT isfinite(@earliest), (@earliest AT TIME ZONE 'UTC')::text,
                   isfinite(@latest), (@latest AT TIME ZONE 'UTC')::text
            """);

        command.Parameters.Add(new NpgsqlParameter("earliest", NpgsqlDbType.TimestampTz)
        {
            Value = DateTimeOffset.MinValue.UtcDateTime
        });
        command.Parameters.Add(new NpgsqlParameter("latest", NpgsqlDbType.TimestampTz)
        {
            Value = DateTimeOffset.MaxValue.UtcDateTime
        });

        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        (await reader.ReadAsync(CancellationToken.None)).ShouldBeTrue();
        reader.GetBoolean(0).ShouldBeTrue();
        reader.GetString(1).ShouldBe("0001-01-01 00:00:00");
        reader.GetBoolean(2).ShouldBeTrue();
        reader.GetString(3).ShouldBe("9999-12-31 23:59:59.999999");
    }

    [DockerFact]
    public async Task Entry_WithTheEarliestInstant_IsStoredAsAnInstantEvenWhenTheApiWouldRefuseIt()
    {
        await using var host = LedgerHost.Create(postgres);
        var accountId = await host.CreateAccountAsync();

        var registered = await host.RegisterAsync(
            accountId,
            $"earliest-{Guid.NewGuid():N}",
            EntryType.Credit,
            10.00m,
            occurredAt: DateTimeOffset.MinValue);

        registered.IsSuccess.ShouldBeTrue();

        var stored = await new WriteTestData(postgres).LastOccurredAtAsync(accountId.Value.ToString());

        stored.IsFinite.ShouldBeTrue();
        stored.Utc.ShouldBe("0001-01-01 00:00:00");
    }
}
