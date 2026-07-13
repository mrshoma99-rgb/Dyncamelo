using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Tests for the loop region — Loop.Item → body → Loop.Collect — which runs the
/// ordinary nodes wired between the boundaries once per item, in order. The key
/// guarantee (which lacing cannot give) is that the whole body runs for item N,
/// in order, before any of it runs for item N+1.
/// </summary>
public class LoopRegionTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    /// <summary>Records "tag:input" in a shared log each time it runs, and passes its input through.</summary>
    private sealed class RecordingNode : NodeModel
    {
        private readonly List<string> _log;
        private readonly string _tag;

        public RecordingNode(List<string> log, string tag)
        {
            _log = log;
            _tag = tag;
            Name = tag;
            AddInput("in", typeof(object));
            AddOutput("out", typeof(object));
        }

        public override string NodeType => "TestRecording";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            _log.Add(_tag + ":" + (inputs[0]?.ToString() ?? "null"));
            return new object?[] { inputs[0] };
        }
    }

    private static LoopItemNode AddLoopItem(GraphModel graph, NodeModel itemsSource, int sourcePort = 0)
    {
        var loop = new LoopItemNode();
        graph.AddNode(loop);
        ZT.Wire(graph, itemsSource, sourcePort, loop, 0);
        return loop;
    }

    private static LoopCollectNode CloseLoop(GraphModel graph, LoopItemNode item, NodeModel valueSource, int valuePort = 0)
    {
        var collect = new LoopCollectNode();
        graph.AddNode(collect);
        ZT.Wire(graph, item, 3, collect, 0);          // loop handle: item.loop -> collect.loop
        ZT.Wire(graph, valueSource, valuePort, collect, 1); // value
        return collect;
    }

    [Fact]
    public void Loop_RunsWholeBodyForEachItem_InOrder()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "a", "b", "c" });
        var loop = AddLoopItem(graph, items);

        var a = new RecordingNode(log, "A");
        var b = new RecordingNode(log, "B");
        graph.AddNode(a);
        graph.AddNode(b);
        ZT.Wire(graph, loop, 0, a, 0);  // loop.item -> A.in
        ZT.Wire(graph, a, 0, b, 0);     // A.out -> B.in (chained: guarantees A before B)
        CloseLoop(graph, loop, b);

        var result = _engine.Run(graph);

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "A:a", "B:a", "A:b", "B:b", "A:c", "B:c" },
            log);
    }

    [Fact]
    public void Loop_CollectsOneValuePerItem()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "x", "y" });
        var loop = AddLoopItem(graph, items);
        var rec = new RecordingNode(log, "R");
        graph.AddNode(rec);
        ZT.Wire(graph, loop, 0, rec, 0);
        var collect = CloseLoop(graph, loop, rec);

        _engine.Run(graph);

        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(collect.OutPorts[0].Value);
        Assert.Equal(new object?[] { "x", "y" }, results.ToArray());
    }

    [Fact]
    public void Loop_RunsRealZeroTouchNodesPerItem()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { 4.0, 16.0, 25.0 });
        var loop = AddLoopItem(graph, items);
        var sqrt = ZT.Node("Sqrt");     // real zero-touch node in the body
        graph.AddNode(sqrt);
        ZT.Wire(graph, loop, 0, sqrt, 0);
        var collect = CloseLoop(graph, loop, sqrt);

        _engine.Run(graph);

        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(collect.OutPorts[0].Value);
        Assert.Equal(new object?[] { 2.0, 4.0, 5.0 }, results.ToArray());
    }

    [Fact]
    public void Loop_ExternalInputToBody_IsBroadcastEachIteration()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { 10.0, 20.0 });
        var step = ZT.Value(graph, 3.0);      // external constant, computed once
        var loop = AddLoopItem(graph, items);
        var addStep = ZT.Node("AddStep");     // AddStep(x, step) = x + step
        graph.AddNode(addStep);
        ZT.Wire(graph, loop, 0, addStep, 0);  // x = current item
        ZT.Wire(graph, step, 0, addStep, 1);  // step = external constant
        var collect = CloseLoop(graph, loop, addStep);

        _engine.Run(graph);

        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(collect.OutPorts[0].Value);
        Assert.Equal(new object?[] { 13.0, 23.0 }, results.ToArray());
    }

    [Fact]
    public void Loop_DownstreamOfResults_RunsOnce_SeeingTheWholeList()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { 1.0, 2.0, 3.0 });
        var loop = AddLoopItem(graph, items);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, loop, 0, sqrt, 0);
        var collect = CloseLoop(graph, loop, sqrt);

        var count = ZT.Node("CountItems"); // CountItems(IList<object>) -> int, runs once after the loop
        graph.AddNode(count);
        ZT.Wire(graph, collect, 0, count, 0);

        _engine.Run(graph);

        Assert.Equal(3, count.OutPorts[0].Value);
    }

    [Fact]
    public void Loop_EmptyItems_CollectsNothing_AndBodyNeverRuns()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?>());
        var loop = AddLoopItem(graph, items);
        var rec = new RecordingNode(log, "R");
        graph.AddNode(rec);
        ZT.Wire(graph, loop, 0, rec, 0);
        var collect = CloseLoop(graph, loop, rec);

        _engine.Run(graph);

        Assert.Empty(log);
        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(collect.OutPorts[0].Value);
        Assert.Empty(results);
    }

    [Fact]
    public void Loop_ScalarItems_RunsExactlyOnce()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, "solo");   // a scalar, not a list
        var loop = AddLoopItem(graph, items);
        var rec = new RecordingNode(log, "R");
        graph.AddNode(rec);
        ZT.Wire(graph, loop, 0, rec, 0);
        CloseLoop(graph, loop, rec);

        _engine.Run(graph);

        Assert.Equal(new[] { "R:solo" }, log);
    }

    [Fact]
    public void UnpairedLoopItem_WarnsWithoutCrashing()
    {
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "a" });
        var loop = AddLoopItem(graph, items); // no Loop.Collect closes it

        var result = _engine.Run(graph);

        Assert.True(result.Success); // no crash
        Assert.Equal(NodeState.Warning, loop.State);
        Assert.Contains(loop.Messages, m => m.Text.Contains("Loop.Collect"));
    }

    [Fact]
    public void NonLoopGraph_StillExecutesNormally()
    {
        // Sanity: the unit ordering must not disturb ordinary graphs.
        var graph = new GraphModel();
        var a = ZT.Value(graph, 9.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);

        _engine.Run(graph);

        Assert.Equal(3.0, sqrt.OutPorts[0].Value);
    }
}
