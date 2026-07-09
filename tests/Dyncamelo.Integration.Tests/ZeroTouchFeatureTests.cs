using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>Zero-touch features: defaulted ports and MultiReturn fan-out, through save/load/run.</summary>
public class ZeroTouchFeatureTests
{
    [Fact]
    public void DefaultedPort_SuppliesDefault_WhenLeftUnconnected()
    {
        // Math.Round(number, digits = 0): "digits" stays unconnected.
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "defaulted-port" };

        var number = new NumberInputNode { Name = "N", Value = 3.75 };
        var round = Pipeline.ZeroTouch(registry, "Math.Round", "Round");

        graph.AddNode(number);
        graph.AddNode(round);
        Pipeline.Connect(graph, number, "value", round, "number");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        var reloadedRound = Pipeline.Node(run, "Round");
        var digitsPort = reloadedRound.InPorts.Single(p => p.Name == "digits");
        Assert.True(digitsPort.UsingDefaultValue); // survives the round-trip
        Assert.Equal(NodeState.Executed, reloadedRound.State);
        Assert.Equal(4d, Pipeline.Output(run, "Round"));
    }

    [Fact]
    public void DefaultedPort_ConnectedValueOverridesDefault()
    {
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "defaulted-port-override" };

        var number = new NumberInputNode { Name = "N", Value = 3.75 };
        var digits = new NumberInputNode { Name = "Digits", Value = 1 }; // double -> int coercion
        var round = Pipeline.ZeroTouch(registry, "Math.Round", "Round");

        graph.AddNode(number);
        graph.AddNode(digits);
        graph.AddNode(round);
        Pipeline.Connect(graph, number, "value", round, "number");
        Pipeline.Connect(graph, digits, "value", round, "digits");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, Pipeline.Node(run, "Round").State);
        Assert.Equal(3.8d, (double)Pipeline.Output(run, "Round")!, precision: 12);
    }

    [Fact]
    public void MultiReturn_FansOutIntoOnePortPerKey()
    {
        // List.FilterByBoolMask returns Dictionary<string, object> with keys "in"/"out".
        var registry = Pipeline.CreateRegistry();
        var graph = new GraphModel { Name = "multi-return" };

        var start = new NumberInputNode { Name = "Start", Value = 1 };
        var end = new NumberInputNode { Name = "End", Value = 4 };
        var range = Pipeline.ZeroTouch(registry, "List.Range", "Range");

        var mask = new ListCreateNode { Name = "Mask" };
        mask.AddItemPort();
        mask.AddItemPort();
        mask.AddItemPort(); // item0..item3
        var t1 = new BooleanToggleNode { Name = "T1", Value = true };
        var f1 = new BooleanToggleNode { Name = "F1", Value = false };
        var t2 = new BooleanToggleNode { Name = "T2", Value = true };
        var f2 = new BooleanToggleNode { Name = "F2", Value = false };

        var filter = Pipeline.ZeroTouch(registry, "List.FilterByBoolMask", "Filter");

        graph.AddNode(start);
        graph.AddNode(end);
        graph.AddNode(range);
        graph.AddNode(mask);
        graph.AddNode(t1);
        graph.AddNode(f1);
        graph.AddNode(t2);
        graph.AddNode(f2);
        graph.AddNode(filter);

        Pipeline.Connect(graph, start, "value", range, "start");
        Pipeline.Connect(graph, end, "value", range, "end");
        Pipeline.Connect(graph, t1, "value", mask, "item0");
        Pipeline.Connect(graph, f1, "value", mask, "item1");
        Pipeline.Connect(graph, t2, "value", mask, "item2");
        Pipeline.Connect(graph, f2, "value", mask, "item3");
        Pipeline.Connect(graph, range, "list", filter, "list");
        Pipeline.Connect(graph, mask, "list", filter, "mask");

        var run = Pipeline.SaveLoadAndRun(graph, registry, out var result);

        Assert.True(result.Success);
        var reloadedFilter = Pipeline.Node(run, "Filter");
        Assert.Equal(NodeState.Executed, reloadedFilter.State);
        Assert.Equal(new[] { "in", "out" }, reloadedFilter.OutPorts.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { 1d, 3d }, Pipeline.AsDoubles(Pipeline.Output(run, "Filter", "in")));
        Assert.Equal(new[] { 2d, 4d }, Pipeline.AsDoubles(Pipeline.Output(run, "Filter", "out")));
    }
}
