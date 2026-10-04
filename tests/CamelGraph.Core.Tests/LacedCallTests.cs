using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Zero-touch nodes that ask whether they are one call of many.</summary>
public static class LacedFixtures
{
    /// <summary>1 when the call is one of several made for one run of the node, else 0.</summary>
    public static double WasLaced(double x) => NodeWarnings.IsLaced ? 1 : 0;

    /// <summary>Takes a whole list; never replicated.</summary>
    public static double WasLacedWhole(IList<double> values) => NodeWarnings.IsLaced ? 1 : 0;

    /// <summary>A node that refuses to be called more than once per run.</summary>
    public static double OncePerRun(double x)
    {
        if (NodeWarnings.IsLaced)
        {
            throw new System.InvalidOperationException("This node changes the model once. Wire a single value.");
        }

        return x;
    }
}

public class LacedCallTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(LacedFixtures));

    private static (GraphModel Graph, ZeroTouchNodeModel Node) Run(string method, object? input)
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, input);
        var node = new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);
        new GraphEngine().Run(graph);
        return (graph, node);
    }

    [Fact]
    public void ASingleCallIsNotLaced()
    {
        var (_, node) = Run("WasLaced", 5.0);

        Assert.Equal(0.0, node.OutPorts[0].Value);
    }

    [Fact]
    public void EveryCallOfAListIsLaced()
    {
        var (_, node) = Run("WasLaced", new List<object?> { 1.0, 2.0, 3.0 });

        Assert.Equal(new object?[] { 1.0, 1.0, 1.0 }, ((IEnumerable<object?>)node.OutPorts[0].Value!).ToArray());
    }

    [Fact]
    public void AListThatIsHandedOverWholeIsASingleCall()
    {
        var (_, node) = Run("WasLacedWhole", new List<object?> { 1.0, 2.0, 3.0 });

        Assert.Equal(0.0, node.OutPorts[0].Value);
    }

    [Fact]
    public void ALacedCallThatThrowsGivesNullAndOneSummaryWarningAndASingleCallStillRuns()
    {
        var (_, laced) = Run("OncePerRun", new List<object?> { 1.0, 2.0 });
        var (_, single) = Run("OncePerRun", 7.0);

        Assert.Equal(NodeState.Warning, laced.State);
        Assert.Contains("changes the model once", laced.StateMessage);
        Assert.Equal(7.0, single.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, single.State);
    }

    [Fact]
    public void OutsideARunNothingIsLaced()
    {
        Assert.False(NodeWarnings.IsLaced);
    }
}
