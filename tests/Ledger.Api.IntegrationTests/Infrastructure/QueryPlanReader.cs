using System.Text.Json;

namespace Ledger.Api.IntegrationTests.Infrastructure;

internal sealed class QueryPlan
{
    private QueryPlan(IReadOnlyList<PlanNode> nodes, long sharedBlocks)
    {
        Nodes = nodes;
        SharedBlocks = sharedBlocks;
    }

    public IReadOnlyList<PlanNode> Nodes { get; }

    public long SharedBlocks { get; }

    public static QueryPlan Parse(string explainJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explainJson);

        using var document = JsonDocument.Parse(explainJson);
        var root = document.RootElement[0].GetProperty("Plan");
        var nodes = new List<PlanNode>();

        Collect(root, nodes);

        return new QueryPlan(nodes, Blocks(root, "Shared Hit Blocks") + Blocks(root, "Shared Read Blocks"));
    }

    public IReadOnlyList<PlanNode> On(string relation) =>
        [.. Nodes.Where(node => string.Equals(node.Relation, relation, StringComparison.Ordinal))];

    public bool Has(string nodeType) =>
        Nodes.Any(node => string.Equals(node.Type, nodeType, StringComparison.Ordinal));

    public override string ToString() =>
        string.Join(" > ", Nodes.Select(node => $"{node.Type}({node.Relation ?? "-"}, {node.Index ?? "-"})"));

    private static long Blocks(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) ? value.GetInt64() : 0;

    private static void Collect(JsonElement node, List<PlanNode> nodes)
    {
        nodes.Add(new PlanNode(
            node.GetProperty("Node Type").GetString() ?? string.Empty,
            Text(node, "Relation Name"),
            Text(node, "Index Name"),
            node.TryGetProperty("Heap Fetches", out var fetches) ? fetches.GetInt64() : 0));

        if (!node.TryGetProperty("Plans", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            Collect(child, nodes);
        }
    }

    private static string? Text(JsonElement node, string name) =>
        node.TryGetProperty(name, out var value) ? value.GetString() : null;
}

internal sealed record PlanNode(string Type, string? Relation, string? Index, long HeapFetches);
