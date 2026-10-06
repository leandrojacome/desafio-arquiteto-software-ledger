namespace Ledger.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class MessagingCollectionDefinition : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "Messaging";
}
