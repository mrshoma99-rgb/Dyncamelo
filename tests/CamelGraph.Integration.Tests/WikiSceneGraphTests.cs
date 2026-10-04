using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.TestSupport.StandIns;
using CamelGraph.TestSupport.Wiki;
using Xunit;

namespace CamelGraph.Integration.Tests;

/// <summary>
/// The graphs the code-made wiki pictures are drawn from (tests/Shared/Wiki/SceneGraphs.cs) do what the pictures say they do: the red node is red,
/// the muted node passes its input on, the group computes what its nodes did. The pictures themselves are drawn by the Windows UI tests.
/// </summary>
public class WikiSceneGraphTests
{
    private static NodeRegistry Registry()
    {
        var registry = Pipeline.CreateRegistry();
        StandInCatalogue.Create(registry).RegisterInto(registry);
        return registry;
    }

    private static NodeModel Named(GraphModel graph, string name) => graph.Nodes.Single(n => n.Name == name);

    [Fact]
    public void ErrorsAndWarningsHasOneRedNodeOneAmberNodeAndIdleNodesBehindTheGate()
    {
        var graph = SceneGraphs.ErrorsAndWarnings(Registry());
        new GraphEngine().Run(graph);

        var failed = Named(graph, "Text to number");
        Assert.Equal(NodeState.Error, failed.State);
        Assert.Equal(SceneGraphs.ErrorText, failed.StateMessage);

        Assert.Equal(NodeState.Warning, Named(graph, "Add one").State);

        Assert.Equal(NodeState.Idle, Named(graph, "Count the numbers").State);
        Assert.Equal(NodeState.Idle, Named(graph, "Count").State);

        Assert.Equal(1, graph.Nodes.Count(n => n.State == NodeState.Error));
        Assert.Equal(1, graph.Nodes.Count(n => n.State == NodeState.Warning));
        Assert.Equal(NodeState.Executed, Named(graph, "Numbers 1 to 5").State);
        Assert.Equal(NodeState.Executed, Named(graph, "Only if Export is on").State);
    }

    [Fact]
    public void AMutedNodePassesItsInputOnAndAFrozenNodeKeepsWhatItHad()
    {
        var graph = SceneGraphs.MuteAndFreeze(Registry());
        var engine = new GraphEngine();
        engine.Run(graph);

        Assert.True(Named(graph, "Times two (muted)").IsMuted);
        Assert.Equal(7d, Named(graph, "Total").OutPorts[0].Value);   // 6 passes through the muted multiply, plus 1
        Assert.Equal(3d, Named(graph, "Side length").OutPorts[0].Value);

        Named(graph, "Side (frozen)").IsFrozen = true;
        ((NumberInputNode)Named(graph, "Area")).Value = 16;
        engine.Run(graph);

        Assert.Equal(3d, Named(graph, "Side length").OutPorts[0].Value);   // the frozen node did not run again
    }

    [Fact]
    public void TheNodeGroupHasItsInputsAndOutputsAndComputesWhatItsNodesDid()
    {
        var graph = SceneGraphs.NodeGroup(Registry(), "Add allowance", out var instance);
        Assert.Equal("Add allowance", instance.Definition!.Name);
        Assert.Equal(1, instance.Definition.Inputs.Count);
        Assert.Equal(1, instance.Definition.Outputs.Count);
        Assert.Equal(2, instance.Definition.Graph.Nodes.Count(n => !(n is GroupInputNode) && !(n is GroupOutputNode)));
        Assert.Equal(3, graph.Nodes.Count);          // Length, the instance and Total: the two calculations are inside the group
        Assert.False(graph.Nodes.Any(n => n.Name == "Times"));

        new GraphEngine().Run(graph);
        Assert.Equal(25d, Named(graph, "Total").OutPorts[0].Value);   // 6 x 2.5 + 10
    }

    [Fact]
    public void TheManyNodesGraphHasEnoughNodesForTheMinimapAndRuns()
    {
        var graph = SceneGraphs.ManyNodes(Registry());
        Assert.Equal(44, graph.Nodes.Count);
        Assert.True(graph.Nodes.Count >= 40);

        new GraphEngine().Run(graph);
        Assert.DoesNotContain(graph.Nodes, n => n.State == NodeState.Error);
        Assert.Equal(2d * 1.5d + 10d, Named(graph, "Result 1").OutPorts[0].Value is double d ? d : double.NaN);
    }

    [Fact]
    public void EverySocketKindHasItsOwnSocket()
    {
        var graph = SceneGraphs.SocketKinds();
        Assert.Equal(15, graph.Nodes.Count);
        var families = graph.Nodes.Select(n => PortKinds.FromPort(n.OutPorts[0]).Family).ToList();
        Assert.Equal(15, families.Distinct().Count());
        Assert.DoesNotContain(PortFamily.Any, families);
        Assert.All(graph.Nodes, n => Assert.Equal(PortDepth.Item, PortKinds.FromPort(n.OutPorts[0]).Depth));
    }

    [Fact]
    public void TheSocketShapesAreASingleValueAListAListOfListsAndAPillWithThreeWires()
    {
        var graph = SceneGraphs.SocketShapes(Registry());
        Assert.Equal(PortDepth.Item, PortKinds.FromPort(Named(graph, "Single value").OutPorts[0]).Depth);
        Assert.Equal(PortDepth.List, PortKinds.FromPort(Named(graph, "List").OutPorts[0]).Depth);
        Assert.Equal(PortDepth.Nested, PortKinds.FromPort(Named(graph, "List of lists").OutPorts[0]).Depth);

        var several = Named(graph, "Several wires");
        Assert.True(several.InPorts[0].IsMultiInput);
        Assert.Equal(3, graph.FindConnectionsInto(several.InPorts[0]).Count);

        new GraphEngine().Run(graph);
        Assert.Equal(27d, several.OutPorts[0].Value);
    }

    [Fact]
    public void ThePropertyValueNodeHasTheMagnifierOnItsTabAndPropertyInputs()
    {
        var graph = SceneGraphs.PropertyValue(Registry());
        var node = graph.Nodes.Single();
        Assert.Equal("Properties.Value", node.Name);
        Assert.Equal(ModelDataKind.Tab, node.FindInPort("categoryName")!.DataChoice!.Kind);
        Assert.Equal(ModelDataKind.Property, node.FindInPort("propertyName")!.DataChoice!.Kind);
        Assert.Equal("item", node.FindInPort("categoryName")!.DataChoice!.From);
        Assert.Null(node.FindInPort("item")!.DataChoice);
    }

    [Fact]
    public void TheAnatomyNodeHasANumberATextADropDownAndTwoColours()
    {
        var graph = SceneGraphs.Anatomy(Registry());
        var node = graph.Nodes.Single();
        Assert.Equal("FallHazard.FloorOpeningMap", node.Name);
        Assert.NotNull(node.FindInPort("units")!.Choices);
        Assert.Equal("colour", node.FindInPort("lowColor")!.KindHint);
        Assert.Equal("#FF2E7D32", node.FindInPort("lowColor")!.UserValue);
        Assert.Equal(3.5d, node.FindInPort("level")!.UserValue);
    }

    [Theory]
    [InlineData("Color Elements by Property", "wiki-sample-color-elements-by-property")]
    [InlineData("Getting Started - Math and Watch", "wiki-sample-getting-started-math-and-watch")]
    [InlineData("Isolated Viewpoints (Loop)", "wiki-sample-isolated-viewpoints-loop")]
    [InlineData("Floor Opening Fall-Hazard Map", "wiki-sample-floor-opening-fall-hazard-map")]
    [InlineData("csv-roundtrip", "wiki-sample-csv-roundtrip")]
    public void ASampleGraphFileGivesItsPictureThisId(string fileName, string expected)
    {
        Assert.Equal(expected, Manifest.SampleImageId(fileName));
    }

    [Fact]
    public void EverySampleGraphHasItsOwnPictureId()
    {
        var ids = System.IO.Directory.GetFiles(SampleGraphFileTests.SamplesDirectory(), "*.dyc")
            .Select(p => Manifest.SampleImageId(System.IO.Path.GetFileNameWithoutExtension(p)))
            .ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheManifestListsTheScenesAndTheHowToGraphsTheTestsDraw()
    {
        var ids = Manifest.ImageIds();
        Assert.Contains("wiki-start-screen", ids);
        Assert.Contains("wiki-node-anatomy", ids);
        Assert.Contains("wiki-settings-privacy", ids);
        Assert.Contains("wiki-quick-search", ids);
        Assert.DoesNotContain(ids, id => id.Contains("<"));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        var graphs = Manifest.GraphNames();
        Assert.Contains("first-script", graphs);
        Assert.Contains("keep-going-after-failure", graphs);
        Assert.Equal(graphs.Count, graphs.Distinct(StringComparer.Ordinal).Count());
    }
}
