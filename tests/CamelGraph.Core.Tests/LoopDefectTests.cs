using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>Fixture nodes for the loop tests.</summary>
public static class LoopFixtures
{
    public static int TwiceCalls;

    public static object Gate(object value, bool condition) => condition ? value : InactiveValue.Instance;

    public static bool IsOdd(double x) => ((int)x) % 2 != 0;

    public static double Twice(double x)
    {
        TwiceCalls++;
        return x * 2;
    }

    public static double MustBePositive(double x)
    {
        if (x < 0)
        {
            throw new ArgumentException("the value must be positive (got " + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        return x * 2;
    }

    public static IList<object> BrokenList(double seed) => throw new InvalidOperationException("the list could not be read");

    public static int CountOf(IList<object> items) => items.Count;
}

/// <summary>Defects of the loop engine: switched-off branches, a failed source, failing iterations.</summary>
public class LoopDefectTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(LoopFixtures));

    private static ZeroTouchNodeModel Node(string method) =>
        new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    private static List<object?> L(params object?[] items) => new List<object?>(items);

    private sealed class Rig
    {
        public GraphModel Graph = new GraphModel();
        public LoopItemNode Item = new LoopItemNode();
        public LoopCollectNode Collect = new LoopCollectNode();
        public NodeModel Body = null!;
    }

    // itemsSource -> Loop.Item -> body -> Loop.Collect, all in rig.Graph (the source is already in the graph).
    private static Rig BuildLoop(GraphModel graph, NodeModel itemsSource, int sourcePort, NodeModel body)
    {
        var rig = new Rig { Graph = graph, Body = body };
        graph.AddNode(rig.Item);
        graph.AddNode(body);
        graph.AddNode(rig.Collect);
        ZT.Wire(graph, itemsSource, sourcePort, rig.Item, 0);
        ZT.Wire(graph, rig.Item, 0, body, 0);
        ZT.Wire(graph, rig.Item, 3, rig.Collect, 0);
        ZT.Wire(graph, body, 0, rig.Collect, 1);
        return rig;
    }

    // ----------------------------------------------------------- SYS-02

    [Fact]
    public void AnIterationSwitchedOffByAWhenIsLeftOutOfTheResults()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, L(1.0, 2.0, 3.0));
        var item = new LoopItemNode();
        var odd = Node("IsOdd");
        var gate = Node("Gate");
        var collect = new LoopCollectNode();
        foreach (var node in new NodeModel[] { item, odd, gate, collect })
        {
            graph.AddNode(node);
        }

        ZT.Wire(graph, items, 0, item, 0);
        ZT.Wire(graph, item, 0, odd, 0);
        ZT.Wire(graph, item, 0, gate, 0);
        ZT.Wire(graph, odd, 0, gate, 1);
        ZT.Wire(graph, item, 3, collect, 0);
        ZT.Wire(graph, gate, 0, collect, 1);

        new GraphEngine().Run(graph);

        Assert.Equal(L(1.0, 3.0), collect.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, collect.State);
        Assert.Empty(collect.Messages);
    }

    [Fact]
    public void ASwitchedOffBranchInFrontOfTheLoopSkipsTheWholeLoop()
    {
        LoopFixtures.TwiceCalls = 0;
        var graph = new GraphModel();
        var items = ZT.Value(graph, L(1.0, 2.0));
        var off = ZT.Value(graph, false);
        var gate = Node("Gate");
        graph.AddNode(gate);
        ZT.Wire(graph, items, 0, gate, 0);
        ZT.Wire(graph, off, 0, gate, 1);
        var rig = BuildLoop(graph, gate, 0, Node("Twice"));
        var after = Node("CountOf");
        graph.AddNode(after);
        ZT.Wire(graph, rig.Collect, 0, after, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(0, LoopFixtures.TwiceCalls);                          // the body never ran
        Assert.Same(InactiveValue.Instance, rig.Collect.OutPorts[0].Value);
        Assert.Equal(NodeState.Idle, rig.Collect.State);
        Assert.Equal(NodeState.Idle, rig.Item.State);
        Assert.Equal(NodeState.Idle, after.State);                          // and what is wired after the loop is skipped too
        Assert.Contains(rig.Collect.Messages, m => m.Text.Contains("Skipped"));
    }

    [Fact]
    public void ALacedWhenGivesNullForTheSwitchedOffElementsNotTheMarker()
    {
        var graph = new GraphModel();
        var value = ZT.Value(graph, 5.0);
        var conditions = ZT.Value(graph, L(true, false, true));
        var gate = Node("Gate");
        graph.AddNode(gate);
        ZT.Wire(graph, value, 0, gate, 0);
        ZT.Wire(graph, conditions, 0, gate, 1);

        new GraphEngine().Run(graph);

        Assert.Equal(L(5.0, null, 5.0), gate.OutPorts[0].Value);
    }

    [Fact]
    public void ASingleWhenStillGivesTheMarker()
    {
        var graph = new GraphModel();
        var value = ZT.Value(graph, 5.0);
        var condition = ZT.Value(graph, false);
        var gate = Node("Gate");
        graph.AddNode(gate);
        ZT.Wire(graph, value, 0, gate, 0);
        ZT.Wire(graph, condition, 0, gate, 1);

        new GraphEngine().Run(graph);

        Assert.Same(InactiveValue.Instance, gate.OutPorts[0].Value);
    }

    // ----------------------------------------------------------- SYS-03

    [Fact]
    public void ALoopWhoseSourceFailedFailsInsteadOfRunningZeroTimesGreen()
    {
        LoopFixtures.TwiceCalls = 0;
        var graph = new GraphModel();
        var seed = ZT.Value(graph, 1.0);
        var broken = Node("BrokenList");
        graph.AddNode(broken);
        ZT.Wire(graph, seed, 0, broken, 0);
        var rig = BuildLoop(graph, broken, 0, Node("Twice"));
        var after = Node("CountOf");
        graph.AddNode(after);
        ZT.Wire(graph, rig.Collect, 0, after, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, broken.State);
        Assert.Equal(0, LoopFixtures.TwiceCalls);
        Assert.Equal(NodeState.Warning, rig.Item.State);
        Assert.True(rig.Item.FailedUpstream);
        Assert.Equal(NodeState.Warning, rig.Collect.State);
        Assert.True(rig.Collect.FailedUpstream);
        Assert.Null(rig.Collect.OutPorts[0].Value);
        Assert.Contains(rig.Collect.Messages, m => m.Text.Contains("Upstream failure"));
        Assert.Equal(NodeState.Warning, after.State);                       // "Upstream failure" like any node after a failure
        Assert.True(after.FailedUpstream);
        Assert.Null(after.OutPorts[0].Value);
    }

    // ----------------------------------------------------------- SYS-04

    [Fact]
    public void FailingIterationsAreCountedAndReportedOnTheCollect()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, L(1.0, -2.0, 3.0, -4.0));
        var rig = BuildLoop(graph, items, 0, Node("MustBePositive"));

        new GraphEngine().Run(graph);

        Assert.Equal(L(2.0, null, 6.0, null), rig.Collect.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, rig.Collect.State);
        var text = string.Join(" ", rig.Collect.Messages.Select(m => m.Text));
        Assert.Contains("2 of 4 iterations failed", text);
        Assert.Contains("item 2", text);
        Assert.Contains("must be positive (got -2)", text);
    }

    [Fact]
    public void ABodyNodeThatFailedInAnEarlierIterationDoesNotLookGreen()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, L(1.0, -2.0, 3.0));          // the last iteration is fine
        var rig = BuildLoop(graph, items, 0, Node("MustBePositive"));

        new GraphEngine().Run(graph);

        Assert.Equal(L(2.0, null, 6.0), rig.Collect.OutPorts[0].Value);
        Assert.Equal(NodeState.Warning, rig.Collect.State);
        Assert.Equal(NodeState.Warning, rig.Body.State);
        Assert.Contains(rig.Body.Messages, m => m.Text.Contains("1 of 3 iterations") && m.Text.Contains("must be positive"));
    }

    [Fact]
    public void ALoopWhereNothingFailsHasNoMessages()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, L(1.0, 2.0));
        var rig = BuildLoop(graph, items, 0, Node("MustBePositive"));

        new GraphEngine().Run(graph);

        Assert.Equal(L(2.0, 4.0), rig.Collect.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, rig.Collect.State);
        Assert.Empty(rig.Collect.Messages);
        Assert.Equal(NodeState.Executed, rig.Body.State);
    }
}
