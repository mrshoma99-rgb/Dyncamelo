using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>List operations and replication (lacing) through the full save/load/run pipeline.</summary>
public class ListAndLacingTests
{
    [Fact]
    public void ListOps_RangeCountIndexUniqueSort()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "list-ops" };

        var start = new NumberInputNode { Name = "Start", Value = 0 };
        var end = new NumberInputNode { Name = "End", Value = 4 };
        var range = Pipeline.ZeroTouch(registry, "List.Range", "Range");
        var count = Pipeline.ZeroTouch(registry, "List.Count", "Count");
        var index = new NumberInputNode { Name = "Index", Value = 2 };
        var itemAt = Pipeline.ZeroTouch(registry, "List.GetItemAtIndex", "ItemAt");

        var dupes = new ListCreateNode { Name = "Dupes" };
        dupes.AddItemPort();
        dupes.AddItemPort(); // item0..item2
        var d1 = new NumberInputNode { Name = "D1", Value = 3 };
        var d2 = new NumberInputNode { Name = "D2", Value = 1 };
        var d3 = new NumberInputNode { Name = "D3", Value = 3 };
        var unique = Pipeline.ZeroTouch(registry, "List.UniqueItems", "Unique");
        var sort = Pipeline.ZeroTouch(registry, "List.Sort", "Sort");

        graph.AddNode(start);
        graph.AddNode(end);
        graph.AddNode(range);
        graph.AddNode(count);
        graph.AddNode(index);
        graph.AddNode(itemAt);
        graph.AddNode(dupes);
        graph.AddNode(d1);
        graph.AddNode(d2);
        graph.AddNode(d3);
        graph.AddNode(unique);
        graph.AddNode(sort);

        Pipeline.Connect(graph, start, "value", range, "start");
        Pipeline.Connect(graph, end, "value", range, "end");
        Pipeline.Connect(graph, range, "list", count, "list");
        Pipeline.Connect(graph, range, "list", itemAt, "list");
        Pipeline.Connect(graph, index, "value", itemAt, "index");
        Pipeline.Connect(graph, d1, "value", dupes, "item0");
        Pipeline.Connect(graph, d2, "value", dupes, "item1");
        Pipeline.Connect(graph, d3, "value", dupes, "item2");
        Pipeline.Connect(graph, dupes, "list", unique, "list");
        Pipeline.Connect(graph, unique, "list", sort, "list");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.All(run.Nodes, n => Assert.Equal(NodeState.Executed, n.State));
        Assert.Equal(new[] { 0d, 1d, 2d, 3d, 4d }, Pipeline.AsDoubles(Pipeline.Output(run, "Range")));
        Assert.Equal(5, Pipeline.Output(run, "Count"));
        Assert.Equal(2d, Pipeline.Output(run, "ItemAt"));
        Assert.Equal(new[] { 3d, 1d }, Pipeline.AsDoubles(Pipeline.Output(run, "Unique")));
        Assert.Equal(new[] { 1d, 3d }, Pipeline.AsDoubles(Pipeline.Output(run, "Sort")));
    }

    [Theory]
    [InlineData(LacingMode.Shortest)]
    [InlineData(LacingMode.Longest)]
    [InlineData(LacingMode.CrossProduct)]
    public void Lacing_TwoListInputsIntoScalarPorts(LacingMode lacing)
    {
        // Add(a: double, b: double) receives [1,2,3] and [10,20]:
        // the excess rank makes the engine replicate per the node's lacing.
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "lacing-" + lacing };

        var startA = new NumberInputNode { Name = "StartA", Value = 1 };
        var endA = new NumberInputNode { Name = "EndA", Value = 3 };
        var rangeA = Pipeline.ZeroTouch(registry, "List.Range", "RangeA");
        var startB = new NumberInputNode { Name = "StartB", Value = 10 };
        var endB = new NumberInputNode { Name = "EndB", Value = 20 };
        var stepB = new NumberInputNode { Name = "StepB", Value = 10 };
        var rangeB = Pipeline.ZeroTouch(registry, "List.Range", "RangeB");
        var add = Pipeline.ZeroTouch(registry, "Add", "LacedAdd");
        add.Lacing = lacing;

        graph.AddNode(startA);
        graph.AddNode(endA);
        graph.AddNode(rangeA);
        graph.AddNode(startB);
        graph.AddNode(endB);
        graph.AddNode(stepB);
        graph.AddNode(rangeB);
        graph.AddNode(add);

        Pipeline.Connect(graph, startA, "value", rangeA, "start");
        Pipeline.Connect(graph, endA, "value", rangeA, "end");
        Pipeline.Connect(graph, startB, "value", rangeB, "start");
        Pipeline.Connect(graph, endB, "value", rangeB, "end");
        Pipeline.Connect(graph, stepB, "value", rangeB, "step");
        Pipeline.Connect(graph, rangeA, "list", add, "a");
        Pipeline.Connect(graph, rangeB, "list", add, "b");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        // Lacing must survive the .dyc round-trip.
        Assert.Equal(lacing, Pipeline.Node(run, "LacedAdd").Lacing);
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "LacedAdd").State);

        var output = Pipeline.Output(run, "LacedAdd");
        switch (lacing)
        {
            case LacingMode.Shortest:
                Assert.Equal(new[] { 11d, 22d }, Pipeline.AsDoubles(output));
                break;
            case LacingMode.Longest:
                // The shorter list repeats its last element ([10,20] -> [10,20,20]).
                Assert.Equal(new[] { 11d, 22d, 23d }, Pipeline.AsDoubles(output));
                break;
            case LacingMode.CrossProduct:
                // Leftmost replicated input is the outermost loop.
                var rows = Pipeline.AsList(output);
                Assert.Equal(3, rows.Count);
                Assert.Equal(new[] { 11d, 21d }, Pipeline.AsDoubles(rows[0]));
                Assert.Equal(new[] { 12d, 22d }, Pipeline.AsDoubles(rows[1]));
                Assert.Equal(new[] { 13d, 23d }, Pipeline.AsDoubles(rows[2]));
                break;
        }
    }
}
