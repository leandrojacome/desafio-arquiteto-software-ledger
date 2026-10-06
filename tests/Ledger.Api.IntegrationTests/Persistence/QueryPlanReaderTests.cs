using Ledger.Api.IntegrationTests.Infrastructure;

namespace Ledger.Api.IntegrationTests.Persistence;

[Trait("Category", "Unit")]
public sealed class QueryPlanReaderTests
{
    private const string BalanceAtPlan = """
        [{
          "Plan": {
            "Node Type": "Nested Loop",
            "Shared Hit Blocks": 5,
            "Shared Read Blocks": 2,
            "Plans": [
              {
                "Node Type": "Seq Scan",
                "Relation Name": "account_balances",
                "Shared Hit Blocks": 1,
                "Shared Read Blocks": 0
              },
              {
                "Node Type": "Limit",
                "Plans": [
                  {
                    "Node Type": "Index Only Scan",
                    "Relation Name": "ledger_entries",
                    "Index Name": "ix_ledger_entries_account_id_recorded_at_account_version",
                    "Heap Fetches": 0,
                    "Shared Hit Blocks": 4,
                    "Shared Read Blocks": 2
                  }
                ]
              }
            ]
          }
        }]
        """;

    private const string SortedPlan = """
        [{
          "Plan": {
            "Node Type": "Limit",
            "Shared Hit Blocks": 10,
            "Plans": [
              {
                "Node Type": "Sort",
                "Plans": [
                  {
                    "Node Type": "Bitmap Heap Scan",
                    "Relation Name": "ledger_entries",
                    "Plans": [
                      { "Node Type": "Bitmap Index Scan", "Index Name": "ix_ledger_entries_recorded_at" }
                    ]
                  }
                ]
              }
            ]
          }
        }]
        """;

    [Fact]
    public void On_ReturnsOnlyTheNodesOfThatRelation_EvenWhenAnotherRelationHasASequentialScan()
    {
        var plan = QueryPlan.Parse(BalanceAtPlan);

        plan.On("ledger_entries").Select(node => node.Type).ShouldBe(["Index Only Scan"]);
        plan.On("account_balances").Select(node => node.Type).ShouldBe(["Seq Scan"]);
        plan.Has("Seq Scan").ShouldBeTrue();
    }

    [Fact]
    public void Parse_ReadsTheIndexAndTheHeapFetchesOfTheNode()
    {
        var node = QueryPlan.Parse(BalanceAtPlan).On("ledger_entries").ShouldHaveSingleItem();

        node.Index.ShouldBe("ix_ledger_entries_account_id_recorded_at_account_version");
        node.HeapFetches.ShouldBe(0);
    }

    [Fact]
    public void Parse_SumsTheHitAndReadBlocksOfTheRootNode()
    {
        QueryPlan.Parse(BalanceAtPlan).SharedBlocks.ShouldBe(7);
    }

    [Fact]
    public void Parse_FindsSortAndBitmapNodesAnywhereInTheTree()
    {
        var plan = QueryPlan.Parse(SortedPlan);

        plan.Has("Sort").ShouldBeTrue();
        plan.Has("Bitmap Heap Scan").ShouldBeTrue();
        plan.Has("Index Only Scan").ShouldBeFalse();
        plan.SharedBlocks.ShouldBe(10);
    }

    [Fact]
    public void Parse_ReadsAMissingBlockCounterAsZero()
    {
        var plan = QueryPlan.Parse("""[{ "Plan": { "Node Type": "Result" } }]""");

        plan.SharedBlocks.ShouldBe(0);
        plan.Nodes.ShouldHaveSingleItem().Type.ShouldBe("Result");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_RefusesAnEmptyDocument(string text)
    {
        Should.Throw<ArgumentException>(() => QueryPlan.Parse(text));
    }
}
