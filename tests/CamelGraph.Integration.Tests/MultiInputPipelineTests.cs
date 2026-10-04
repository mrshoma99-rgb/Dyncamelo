using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;
using CamelGraph.Nodes;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>Multi-input sockets through the full save / load / run pipeline.</summary>
public class MultiInputPipelineTests
{
    [Fact]
    public void ListMergeTakesAnyNumberOfWiresAndTheirOrderSurvivesSaveAndLoad()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "merge" };
        var merge = Pipeline.ZeroTouch(registry, "List.Merge", "Merge");
        graph.AddNode(merge);
        Assert.True(merge.InPorts[0].IsMultiInput);

        foreach (var (name, value) in new[] { ("Third", 30d), ("First", 10d), ("Second", 20d) })
        {
            var number = new NumberInputNode { Name = name, Value = value };
            graph.AddNode(number);
        }

        // Wire in the order First, Second, Third — not creation order.
        foreach (var name in new[] { "First", "Second", "Third" })
        {
            Pipeline.Connect(graph, Pipeline.Node(graph, name), "value", merge, "lists");
        }

        var loaded = Pipeline.SaveLoadAndRun(graph, registry, out _);

        Assert.Equal(new[] { 10d, 20d, 30d }, Pipeline.AsDoubles(Pipeline.Output(loaded, "Merge")));
    }

    [Fact]
    public void AListWireAndSingleValuesCombine()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "merge-mixed" };
        var merge = Pipeline.ZeroTouch(registry, "List.Merge", "Merge");
        var range = Pipeline.ZeroTouch(registry, "List.Range", "Range");
        var start = new NumberInputNode { Name = "Start", Value = 1 };
        var end = new NumberInputNode { Name = "End", Value = 3 };
        var extra = new NumberInputNode { Name = "Extra", Value = 99 };
        foreach (var node in new NodeModel[] { merge, range, start, end, extra })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, start, "value", range, range.InPorts[0].Name);
        Pipeline.Connect(graph, end, "value", range, range.InPorts[1].Name);
        Pipeline.Connect(graph, range, range.OutPorts[0].Name, merge, "lists");
        Pipeline.Connect(graph, extra, "value", merge, "lists");

        var loaded = Pipeline.SaveLoadAndRun(graph, registry, out _);

        var result = Pipeline.AsDoubles(Pipeline.Output(loaded, "Merge"));
        Assert.Equal(99d, result.Last());
        Assert.True(result.Count >= 2);
    }

    [Fact]
    public void AllTrueStillWorksWithAnOrdinarySingleWire()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "alltrue" };
        var allTrue = Pipeline.ZeroTouch(registry, "List.AllTrue", "AllTrue");
        var flags = new ListCreateNode { Name = "Flags" };
        flags.AddItemPort();
        var a = new BooleanToggleNode { Name = "A", Value = true };
        var b = new BooleanToggleNode { Name = "B", Value = true };
        foreach (var node in new NodeModel[] { allTrue, flags, a, b })
        {
            graph.AddNode(node);
        }

        Pipeline.Connect(graph, a, "value", flags, "item0");
        Pipeline.Connect(graph, b, "value", flags, "item1");
        Pipeline.Connect(graph, flags, "list", allTrue, "list");

        var loaded = Pipeline.SaveLoadAndRun(graph, registry, out _);

        Assert.Equal(true, Pipeline.Output(loaded, "AllTrue"));
    }

    [Fact]
    public void AllTrueOverSeveralBooleanWiresAtOnce()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "alltrue-multi" };
        var allTrue = Pipeline.ZeroTouch(registry, "List.AllTrue", "AllTrue");
        graph.AddNode(allTrue);
        var toggles = new[] { true, true, false }.Select((v, i) => new BooleanToggleNode { Name = "T" + i, Value = v }).ToList();
        foreach (var toggle in toggles)
        {
            graph.AddNode(toggle);
            Pipeline.Connect(graph, toggle, "value", allTrue, "list");
        }

        Assert.Equal(false, Pipeline.Output(Pipeline.SaveLoadAndRun(graph, registry, out _), "AllTrue"));

        toggles[2].Value = true;
        Assert.Equal(true, Pipeline.Output(Pipeline.SaveLoadAndRun(graph, registry, out _), "AllTrue"));
    }
}
