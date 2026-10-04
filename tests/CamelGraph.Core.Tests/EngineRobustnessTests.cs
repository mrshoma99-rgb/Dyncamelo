using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Fixture nodes that misbehave in ways the engine has to survive.</summary>
public static class RobustnessFixtures
{
    public static double Greedy(double x) => throw new OutOfMemoryException("Array dimensions exceeded supported range.");

    public static double Finite(double x)
    {
        NodeWarnings.WarnIfNotFinite(x, "The input");
        return x;
    }
}

/// <summary>An out-of-memory node is that node's error; the run goes on. Non-finite numbers can be reported as warnings.</summary>
public class EngineRobustnessTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(RobustnessFixtures));

    private static ZeroTouchNodeModel Node(string method) =>
        new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    [Fact]
    public void AnOutOfMemoryNodeIsAnErrorOnThatNodeAndTheRunGoesOn()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);
        var greedy = Node("Greedy");
        var after = ZT.Node("Sqrt");
        var independent = ZT.Node("Sqrt");
        var four = ZT.Value(graph, 4.0);
        graph.AddNode(greedy);
        graph.AddNode(after);
        graph.AddNode(independent);
        ZT.Wire(graph, source, 0, greedy, 0);
        ZT.Wire(graph, greedy, 0, after, 0);
        ZT.Wire(graph, four, 0, independent, 0);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, greedy.State);
        Assert.Contains("Ran out of memory", greedy.StateMessage);
        Assert.Null(greedy.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, after.State);                      // stopped by the failure upstream, like any failure
        Assert.Equal(NodeState.Executed, independent.State);               // the rest of the graph ran
        Assert.Equal(2d, independent.OutPorts[0].Value);
    }

    [Fact]
    public void AnOutOfMemoryElementOfALacedNodeIsStillAnErrorNotACrash()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, new List<object?> { 1.0, 2.0 });
        var greedy = Node("Greedy");
        graph.AddNode(greedy);
        ZT.Wire(graph, source, 0, greedy, 0);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, greedy.State);
        Assert.Contains("Ran out of memory", greedy.StateMessage);
    }

    [Fact]
    public void ANodeCanWarnAboutANumberThatIsNotFinite()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, new List<object?> { 1.0, double.NaN, double.PositiveInfinity, 4.0 });
        var node = Node("Finite");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Warning, node.State);
        var message = Assert.Single(node.Messages);
        Assert.Equal("2 of 4 calls: The input is not a finite number (NaN).", message.Text);
    }

    [Fact]
    public void WarnIfNotFiniteSaysWhatItDidAndIsHarmlessOutsideARun()
    {
        Assert.False(NodeWarnings.WarnIfNotFinite(1.5, "x"));
        Assert.True(NodeWarnings.WarnIfNotFinite(double.NegativeInfinity, "x"));   // no run: nothing to report to, no throw
    }
}
