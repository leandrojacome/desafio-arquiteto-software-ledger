namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class DatabaseWorkerFactory(PostgresFixture postgres) : WorkerFactory(postgres.Configuration);
