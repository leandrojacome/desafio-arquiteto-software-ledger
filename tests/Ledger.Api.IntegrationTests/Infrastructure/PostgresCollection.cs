namespace Ledger.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
