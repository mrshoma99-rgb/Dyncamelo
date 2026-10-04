using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Nodes whose rarely used inputs sit in a foldable panel.</summary>
public static class PanelFixtures
{
    [NodeName("Panel.Report")]
    public static double Report(
        double size,
        string label = "x",
        [NodePanel("Advanced")] double tolerance = 0.5,
        [NodePanel("Advanced")] int digits = 2)
        => size * 100 + tolerance * 10 + digits;

    [NodeName("Panel.Scaled")]
    public static double Scaled(double amount, [NodePanel("Options", DefaultOpen = true)] double scale = 1)
        => amount * scale;
}

/// <summary>
/// [NodePanel("Advanced")] end to end (SYS-20): the loader puts the input in a panel, the row planner groups the panelled inputs
/// under one header that starts collapsed, a value saved in a collapsed panel still reaches the node, and which panels the user
/// opened or closed survives saving the graph.
/// </summary>
public class NodePanelTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(PanelFixtures));

    private static ZeroTouchNodeModel Node(string method) => new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    private static string Sig(IEnumerable<PlannedRow> rows) =>
        string.Join(" ", rows.Select(r => r.Key + (r.ZeroHeight ? "!" : string.Empty)));

    private static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        registry.RegisterAssembly(typeof(PanelFixtures).Assembly);
        return registry;
    }

    private static GraphModel RoundTrip(GraphModel graph)
    {
        var serializer = new GraphSerializer(Registry());
        return serializer.Deserialize(serializer.Serialize(graph));
    }

    [Fact]
    public void ThePanelledInputsAreGroupedUnderOneHeaderThatStartsCollapsed()
    {
        var node = Node("Report");

        Assert.Equal("Advanced", node.InPorts.Single(p => p.Name == "tolerance").Panel);
        Assert.Equal("Advanced", node.InPorts.Single(p => p.Name == "digits").Panel);
        Assert.Equal(string.Empty, node.InPorts.Single(p => p.Name == "size").Panel);

        var rows = RowPlanner.Plan(node, p => false, hasBody: false);

        // the main inputs, then one "Advanced" header, closed, with no member rows
        Assert.Equal("o:result i:size i:label p:Advanced", Sig(rows));
        var header = rows.Single(r => r.Kind == RowKind.PanelHeader);
        Assert.False(header.IsOpen);
        Assert.False(header.PanelDefaultOpen);
        Assert.Equal(0, header.Count);
    }

    [Fact]
    public void OpeningThePanelShowsItsInputsAndAPanelMarkedDefaultOpenStartsOpen()
    {
        var node = Node("Report");
        RowPlanner.SetPanelOpen(node, "Advanced", defaultOpen: false, open: true);

        Assert.Equal("o:result i:size i:label p:Advanced i:tolerance i:digits", Sig(RowPlanner.Plan(node, p => false, false)));

        var open = Node("Scaled");
        Assert.Equal("o:result i:amount p:Options i:scale", Sig(RowPlanner.Plan(open, p => false, false)));
        Assert.True(RowPlanner.Plan(open, p => false, false).Single(r => r.Kind == RowKind.PanelHeader).IsOpen);
    }

    [Fact]
    public void AWiredInputInAClosedPanelKeepsAZeroHeightRowSoItsWireHasAnAnchor()
    {
        var node = Node("Report");
        var wired = node.InPorts.Single(p => p.Name == "digits");

        var rows = RowPlanner.Plan(node, p => p == wired, hasBody: false);

        Assert.Equal("o:result i:size i:label p:Advanced i:digits!", Sig(rows));
    }

    [Fact]
    public void ASavedValueInACollapsedPanelStillReachesTheNodeAndTheHeaderCountsIt()
    {
        var graph = new GraphModel();
        var node = Node("Report");
        graph.AddNode(node);
        node.InPorts.Single(p => p.Name == "size").SetUserValue(2d);
        node.InPorts.Single(p => p.Name == "tolerance").SetUserValue(3d);
        node.InPorts.Single(p => p.Name == "digits").SetUserValue(7);

        // the panel is closed: no member rows, but the header says two values are set
        var header = RowPlanner.Plan(node, p => false, false).Single(r => r.Kind == RowKind.PanelHeader);
        Assert.False(header.IsOpen);
        Assert.Equal(2, header.Count);
        Assert.DoesNotContain(RowPlanner.Plan(node, p => false, false), r => r.Key == "i:tolerance");

        var loaded = RoundTrip(graph);
        var result = new GraphEngine().Run(loaded);

        Assert.True(result.Success);
        var report = loaded.Nodes.Single();
        Assert.Equal(2d * 100 + 3d * 10 + 7, report.OutPorts[0].Value);
        Assert.Equal(2, RowPlanner.Plan(report, p => false, false).Single(r => r.Kind == RowKind.PanelHeader).Count);
    }

    [Fact]
    public void ADefaultInAClosedPanelIsUsedWhenNothingIsSet()
    {
        var graph = new GraphModel();
        var node = Node("Report");
        graph.AddNode(node);
        node.InPorts.Single(p => p.Name == "size").SetUserValue(1d);

        new GraphEngine().Run(graph);

        Assert.Equal(1d * 100 + 0.5 * 10 + 2, node.OutPorts[0].Value);
    }

    [Fact]
    public void ThePanelsTheUserOpenedStayOpenAfterSavingAndLoading()
    {
        var graph = new GraphModel();
        var node = Node("Report");
        graph.AddNode(node);
        RowPlanner.SetPanelOpen(node, "Advanced", defaultOpen: false, open: true);

        var loaded = RoundTrip(graph).Nodes.Single();

        Assert.True(RowPlanner.IsPanelOpen(loaded, "Advanced", defaultOpen: false));
        Assert.Contains("i:tolerance", RowPlanner.Plan(loaded, p => false, false).Select(r => r.Key));
    }

    [Fact]
    public void APanelThatStartsOpenAndWasClosedByTheUserStaysClosedAfterSavingAndLoading()
    {
        var graph = new GraphModel();
        var node = Node("Scaled");
        graph.AddNode(node);
        RowPlanner.SetPanelOpen(node, "Options", defaultOpen: true, open: false);
        Assert.False(node.Ui.IsDefault);

        var loaded = RoundTrip(graph).Nodes.Single();

        Assert.False(RowPlanner.IsPanelOpen(loaded, "Options", defaultOpen: true));
        Assert.DoesNotContain("i:scale", RowPlanner.Plan(loaded, p => false, false).Select(r => r.Key));
    }
}
