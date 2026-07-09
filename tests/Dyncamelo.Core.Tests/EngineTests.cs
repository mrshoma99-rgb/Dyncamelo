using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

public class EngineTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    [Fact]
    public void DiamondGraph_ExecutesInTopologicalOrder_AndComputesCorrectValue()
    {
        // a -> sqrt(b), a -> negate-ish (addStep, c); b + c -> d
        var graph = new GraphModel();
        var a = ZT.Value(graph, 16.0);
        var b = ZT.Node("Sqrt");
        var c = ZT.Node("AddStep"); // x + 1
        var d = ZT.Node("Add");
        graph.AddNode(b);
        graph.AddNode(c);
        graph.AddNode(d);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, a, 0, c, 0);
        ZT.Wire(graph, b, 0, d, 0);
        ZT.Wire(graph, c, 0, d, 1);

        var result = _engine.Run(graph);

        Assert.True(result.Success);
        Assert.Equal(4, result.ExecutedNodes.Count);
        var order = result.ExecutedNodes.ToList();
        Assert.True(order.IndexOf(a) < order.IndexOf(b));
        Assert.True(order.IndexOf(a) < order.IndexOf(c));
        Assert.True(order.IndexOf(b) < order.IndexOf(d));
        Assert.True(order.IndexOf(c) < order.IndexOf(d));
        Assert.Equal(21.0, (double)d.OutPorts[0].Value!); // sqrt(16) + (16+1)
        Assert.Equal(NodeState.Executed, d.State);
    }

    [Fact]
    public void SecondRun_WithNoChanges_ExecutesNothing()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 4.0);
        var b = ZT.Node("Sqrt");
        graph.AddNode(b);
        ZT.Wire(graph, a, 0, b, 0);

        _engine.Run(graph);
        var second = _engine.Run(graph);

        Assert.Empty(second.ExecutedNodes);
        Assert.Equal(2.0, (double)b.OutPorts[0].Value!); // cache survives
    }

    [Fact]
    public void ChangingOneInput_ReExecutesOnlyItsTransitiveDownstream()
    {
        var graph = new GraphModel();
        var a = new NumberInputNode { Value = 4 };
        graph.AddNode(a);
        var other = new NumberInputNode { Value = 10 };
        graph.AddNode(other);
        var add = ZT.Node("Add");
        graph.AddNode(add);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        var unrelated = new NumberInputNode { Value = 99 };
        graph.AddNode(unrelated);
        ZT.Wire(graph, a, 0, add, 0);
        ZT.Wire(graph, other, 0, add, 1);
        ZT.Wire(graph, add, 0, sqrt, 0);
        _engine.Run(graph);

        a.Value = 6;
        var result = _engine.Run(graph);

        var executed = result.ExecutedNodes.ToHashSet();
        Assert.Equal(new HashSet<NodeModel> { a, add, sqrt }, executed);
        Assert.DoesNotContain(other, executed);
        Assert.DoesNotContain(unrelated, executed);
        Assert.Equal(4.0, (double)sqrt.OutPorts[0].Value!); // sqrt(6 + 10)
    }

    [Fact]
    public void MissingRequiredInput_LeavesNodeIdle_WithInfoMessage_AndNullOutputs()
    {
        var graph = new GraphModel();
        var add = ZT.Node("Add");
        graph.AddNode(add);

        _engine.Run(graph);

        Assert.Equal(NodeState.Idle, add.State);
        Assert.Null(add.OutPorts[0].Value);
        Assert.Contains(add.Messages, m => m.Severity == MessageSeverity.Info && m.Text.Contains("'a'"));
        Assert.False(add.IsDirty); // clean until a mutation re-dirties it
    }

    [Fact]
    public void OptionalParameter_UsesDefault_WhenUnconnected_AndConnectionOverrides()
    {
        var graph = new GraphModel();
        var x = ZT.Value(graph, 5.0);
        var addStep = ZT.Node("AddStep");
        graph.AddNode(addStep);
        ZT.Wire(graph, x, 0, addStep, 0);

        _engine.Run(graph);
        Assert.Equal(6.0, (double)addStep.OutPorts[0].Value!); // default step = 1.0

        var step = ZT.Value(graph, 10.0);
        ZT.Wire(graph, step, 0, addStep, 1);
        _engine.Run(graph);
        Assert.Equal(15.0, (double)addStep.OutPorts[0].Value!);
    }

    [Fact]
    public void FailingNode_IsIsolated_DownstreamWarns_SiblingBranchStillExecutes()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var fail = ZT.Node("Fail");
        var afterFail = ZT.Node("Sqrt");
        var sibling = ZT.Node("Sqrt");
        graph.AddNode(fail);
        graph.AddNode(afterFail);
        graph.AddNode(sibling);
        ZT.Wire(graph, a, 0, fail, 0);
        ZT.Wire(graph, fail, 0, afterFail, 0);
        ZT.Wire(graph, a, 0, sibling, 0);

        var result = _engine.Run(graph);

        Assert.True(result.Success); // node failures never abort the run
        Assert.Equal(NodeState.Error, fail.State);
        Assert.Contains("boom", fail.StateMessage);
        Assert.Null(fail.OutPorts[0].Value);

        Assert.Equal(NodeState.Warning, afterFail.State);
        Assert.Contains("Upstream failure", afterFail.StateMessage);
        Assert.Null(afterFail.OutPorts[0].Value);

        Assert.Equal(NodeState.Executed, sibling.State);
        Assert.Equal(1.0, (double)sibling.OutPorts[0].Value!);
    }

    [Fact]
    public void SelfReportedErrorMessage_SurfacesAsErrorState_AndDownstreamWarns()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 1.0);
        var failing = new SelfReportedErrorNode();
        graph.AddNode(failing);
        var downstream = ZT.Node("Sqrt");
        graph.AddNode(downstream);
        ZT.Wire(graph, a, 0, failing, 0);
        ZT.Wire(graph, failing, 0, downstream, 0);

        var result = _engine.Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, failing.State);
        Assert.Contains("self-reported failure", failing.StateMessage);
        Assert.Equal(NodeState.Warning, downstream.State);
        Assert.Contains("Upstream failure", downstream.StateMessage);
    }

    [Fact]
    public void FrozenNode_AndDownstream_AreSkipped_AndStayDirty()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 4.0);
        var b = ZT.Node("Sqrt");
        var c = ZT.Node("Sqrt");
        graph.AddNode(b);
        graph.AddNode(c);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, b, 0, c, 0);
        _engine.Run(graph);

        b.IsFrozen = true;
        ((ValueNode)a).Value = 16.0;
        var frozenRun = _engine.Run(graph);

        Assert.Contains(a, frozenRun.ExecutedNodes);
        Assert.DoesNotContain(b, frozenRun.ExecutedNodes);
        Assert.DoesNotContain(c, frozenRun.ExecutedNodes);
        Assert.True(b.IsDirty);
        Assert.True(c.IsDirty);
        Assert.Equal(2.0, (double)b.OutPorts[0].Value!); // stale cached value

        b.IsFrozen = false;
        var thawedRun = _engine.Run(graph);
        Assert.Contains(b, thawedRun.ExecutedNodes);
        Assert.Contains(c, thawedRun.ExecutedNodes);
        Assert.Equal(2.0, (double)c.OutPorts[0].Value!); // sqrt(sqrt(16))
    }

    [Fact]
    public void CancelledToken_StopsBetweenNodes_AndLeavesRemainderDirty()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 4.0);
        var b = ZT.Node("Sqrt");
        graph.AddNode(b);
        ZT.Wire(graph, a, 0, b, 0);

        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = _engine.Run(graph, new EvaluationContext(source.Token));

        Assert.True(result.Cancelled);
        Assert.False(result.Success);
        Assert.Empty(result.ExecutedNodes);
        Assert.True(a.IsDirty);
        Assert.True(b.IsDirty);

        var resumed = _engine.Run(graph);
        Assert.Equal(2, resumed.ExecutedNodes.Count);
        Assert.Equal(2.0, (double)b.OutPorts[0].Value!);
    }

    [Fact]
    public void ReentrantRun_Throws_AndIsSurfacedAsNodeError()
    {
        var graph = new GraphModel();
        var reentrant = new ReentrantNode(_engine);
        graph.AddNode(reentrant);
        reentrant.TargetGraph = graph;

        var result = _engine.Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, reentrant.State);
        Assert.Contains("already running", reentrant.StateMessage);
    }

    [Fact]
    public void MultiReturn_MapsDictionaryToPorts()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 17);
        var b = ZT.Value(graph, 5);
        var divMod = ZT.Node("DivMod");
        graph.AddNode(divMod);
        ZT.Wire(graph, a, 0, divMod, 0);
        ZT.Wire(graph, b, 0, divMod, 1);

        _engine.Run(graph);

        Assert.Equal(NodeState.Executed, divMod.State);
        Assert.Equal("quotient", divMod.OutPorts[0].Name);
        Assert.Equal("remainder", divMod.OutPorts[1].Name);
        Assert.Equal(3, (int)divMod.OutPorts[0].Value!);
        Assert.Equal(2, (int)divMod.OutPorts[1].Value!);
    }

    [Fact]
    public void MultiReturn_MissingKey_YieldsNullAndWarning()
    {
        var graph = new GraphModel();
        var onlyA = ZT.Node("OnlyA");
        graph.AddNode(onlyA);

        _engine.Run(graph);

        Assert.Equal(NodeState.Warning, onlyA.State);
        Assert.Equal(1, (int)onlyA.OutPorts[0].Value!);
        Assert.Null(onlyA.OutPorts[1].Value);
        Assert.Contains("'b'", onlyA.StateMessage);
    }

    [Fact]
    public void VoidMethod_PassesFirstInputThrough()
    {
        var graph = new GraphModel();
        var payload = new object();
        var a = ZT.Value(graph, payload);
        var consume = ZT.Node("Consume");
        graph.AddNode(consume);
        ZT.Wire(graph, a, 0, consume, 0);

        _engine.Run(graph);

        Assert.Equal(NodeState.Executed, consume.State);
        Assert.Same(payload, consume.OutPorts[0].Value);
    }

    [Fact]
    public void WatchNode_FormatsValue_AndPassesThrough()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, new List<object?> { 1.0, 2.5, null });
        var watch = new WatchNode();
        graph.AddNode(watch);
        ZT.Wire(graph, a, 0, watch, 0);

        _engine.Run(graph);

        Assert.Equal("[1, 2.5, null]", watch.FormattedValue);
        Assert.Equal(new List<object?> { 1.0, 2.5, null }, (List<object?>)watch.OutPorts[0].Value!);
    }

    [Fact]
    public void EnumParameter_AcceptsStringName()
    {
        var graph = new GraphModel();
        var input = new StringInputNode { Value = "winter" };
        graph.AddNode(input);
        var seasonName = ZT.Node("SeasonName");
        graph.AddNode(seasonName);
        ZT.Wire(graph, input, 0, seasonName, 0);

        _engine.Run(graph);

        Assert.Equal("Winter", (string)seasonName.OutPorts[0].Value!);
    }

    [Fact]
    public void NodeStateChanged_EventFires()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 4.0);
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, a, 0, sqrt, 0);
        int fired = 0;
        sqrt.NodeStateChanged += (_, _) => fired++;

        _engine.Run(graph);

        Assert.True(fired > 0);
        Assert.Equal(NodeState.Executed, sqrt.State);
    }

    [Fact]
    public void NestedLazyEnumerableOutput_ReplicatesIntoScalarPort()
    {
        // LazyGrid returns IEnumerable<IEnumerable<double>> built from LINQ
        // iterators; recursive materialization must let a scalar double port
        // replicate over both levels.
        var graph = new GraphModel();
        var grid = ZT.Node("LazyGrid");
        var addStep = ZT.Node("AddStep"); // double x, step = 1.0
        graph.AddNode(grid);
        graph.AddNode(addStep);
        ZT.Wire(graph, grid, 0, addStep, 0);

        _engine.Run(graph);

        Assert.Equal(NodeState.Executed, addStep.State);
        Assert.DoesNotContain(addStep.Messages, m => m.Severity == MessageSeverity.Warning);
        var outer = Assert.IsAssignableFrom<System.Collections.IList>(addStep.OutPorts[0].Value);
        Assert.Equal(2, outer.Count);
        Assert.Equal(
            new object?[] { 1.0, 2.0, 3.0 },
            ((System.Collections.IList)outer[0]!).Cast<object?>().ToArray());
        Assert.Equal(
            new object?[] { 11.0, 12.0, 13.0 },
            ((System.Collections.IList)outer[1]!).Cast<object?>().ToArray());
    }

    [Fact]
    public void NestedLazyEnumerableOutput_CoercesIntoListPort()
    {
        var graph = new GraphModel();
        var grid = ZT.Node("LazyGrid");
        var sum = ZT.Node("Sum"); // IList<double>
        graph.AddNode(grid);
        graph.AddNode(sum);
        ZT.Wire(graph, grid, 0, sum, 0);

        _engine.Run(graph);

        Assert.Equal(NodeState.Executed, sum.State);
        Assert.DoesNotContain(sum.Messages, m => m.Severity == MessageSeverity.Warning);
        var results = Assert.IsAssignableFrom<System.Collections.IList>(sum.OutPorts[0].Value);
        Assert.Equal(new object?[] { 3.0, 33.0 }, results.Cast<object?>().ToArray());
    }

    [Fact]
    public void OptionalStructParameter_DeclaredDefault_ExecutesWithDefaultOfT()
    {
        // "DateTime when = default" / "Guid id = default" store no compile-time
        // constant; the loader must synthesize default(T) so the node can run.
        var graph = new GraphModel();
        var text = ZT.Value(graph, "hi");
        var stamp = ZT.Node("StampIt");
        var guid = ZT.Node("GuidThing");
        graph.AddNode(stamp);
        graph.AddNode(guid);
        ZT.Wire(graph, text, 0, stamp, 0);

        _engine.Run(graph);

        Assert.Equal(NodeState.Executed, stamp.State);
        Assert.Equal("hi|0", stamp.OutPorts[0].Value); // default(DateTime).Ticks == 0
        Assert.Equal(NodeState.Executed, guid.State);
        Assert.Equal(Guid.Empty, guid.OutPorts[0].Value);
    }

    private class ReentrantNode : NodeModel
    {
        private readonly GraphEngine _engine;

        public ReentrantNode(GraphEngine engine)
        {
            _engine = engine;
            Name = "Reentrant";
            AddOutput("result", typeof(object));
        }

        public GraphModel? TargetGraph { get; set; }

        public override string NodeType => "TestReentrant";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            _engine.Run(TargetGraph!);
            return new object?[] { null };
        }
    }
}
