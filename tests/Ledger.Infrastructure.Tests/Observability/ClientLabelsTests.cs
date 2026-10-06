using Ledger.Infrastructure.Observability;

namespace Ledger.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public sealed class ClientLabelsTests
{
    [Theory]
    [InlineData("pix-gateway")]
    [InlineData("cards.core")]
    [InlineData("svc:billing")]
    [InlineData("bot@internal")]
    [InlineData("A_1")]
    public void Resolve_WithASafeIdentifier_KeepsIt(string clientId)
    {
        new ClientLabels().Resolve(clientId).ShouldBe(clientId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("comma,separated")]
    [InlineData("quote\"")]
    [InlineData("new\nline")]
    [InlineData("café")]
    [InlineData("123.456.789-09")]
    [InlineData("12345678909")]
    [InlineData("12.345.678/0001-95")]
    [InlineData("password=hunter2")]
    public void Resolve_WithAnUnsafeOrMissingIdentifier_ReturnsUnknown(string? clientId)
    {
        new ClientLabels().Resolve(clientId).ShouldBe(ClientLabels.Unknown);
    }

    [Fact]
    public void Resolve_WithMoreThanTheMaximumLength_ReturnsUnknown()
    {
        new ClientLabels().Resolve(new string('a', ClientLabels.MaxLength + 1)).ShouldBe(ClientLabels.Unknown);
        new ClientLabels().Resolve(new string('a', ClientLabels.MaxLength)).Length.ShouldBe(ClientLabels.MaxLength);
    }

    [Fact]
    public void Resolve_BeyondTheCapacity_FoldsNewClientsIntoOtherAndKeepsTheKnownOnes()
    {
        var labels = new ClientLabels(2);

        labels.Resolve("one").ShouldBe("one");
        labels.Resolve("two").ShouldBe("two");
        labels.Resolve("three").ShouldBe(ClientLabels.Overflow);
        labels.Resolve("one").ShouldBe("one");
        labels.Resolve("two").ShouldBe("two");
    }

    [Fact]
    public async Task Resolve_UnderParallelCallers_NeverAdmitsMoreDistinctClientsThanTheCapacity()
    {
        const int capacity = 16;
        var labels = new ClientLabels(capacity);
        var tasks = Enumerable.Range(0, 64)
            .Select(worker => Task.Run(
                () => Enumerable.Range(0, 200).Select(index => labels.Resolve($"client-{worker}-{index}")).ToList()));

        var results = (await Task.WhenAll(tasks)).SelectMany(batch => batch).ToList();

        var admitted = results.Where(label => label is not ClientLabels.Overflow).Distinct().ToList();
        admitted.Count.ShouldBe(capacity);
    }
}
