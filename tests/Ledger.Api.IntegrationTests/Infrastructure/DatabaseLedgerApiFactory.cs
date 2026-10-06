namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class DatabaseLedgerApiFactory(PostgresFixture postgres) : LedgerApiFactory(postgres.Configuration);
