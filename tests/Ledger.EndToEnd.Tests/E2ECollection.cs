namespace Ledger.EndToEnd.Tests;

[CollectionDefinition(Name)]
public sealed class E2ECollectionDefinition : ICollectionFixture<E2EFixture>
{
    public const string Name = "ComposeStack";
}
