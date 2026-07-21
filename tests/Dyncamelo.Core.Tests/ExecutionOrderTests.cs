using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Scheduling contract for nodes whose data dependencies leave their order
/// open (parallel side-effect branches): the order is UNSPECIFIED but must be
/// deterministic — identical from run to run of the same graph — matching the
/// policy of Grasshopper (document order) and Dynamo (undefined branch order,
/// pinned via Passthrough). The supported way to force an order is a data
/// dependency: chain pass-through outputs, or use Flow.Then.
/// </summary>
public class ExecutionOrderTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    private sealed class RecordingNode : NodeModel
    {
        private readonly List<string> _log;
        private readonly string _tag;

        public RecordingNode(List<string> log, string tag)
        {
            _log = log;
            _tag = tag;
            Name = tag;
            AddInput("in", typeof(object), defaultValue: null);
            AddOutput("out", typeof(object));
        }

        public override string NodeType => "TestRecording";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            _log.Add(_tag);
            return new object?[] { inputs[0] };
        }

        public void Touch() => MarkDirty();
    }

    [Fact]
    public void IndependentBranches_OrderIsStableAcrossRuns()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);

        var a = new RecordingNode(log, "A");
        var b = new RecordingNode(log, "B");
        var c = new RecordingNode(log, "C");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(c);
        ZT.Wire(graph, source, 0, a, 0);
        ZT.Wire(graph, source, 0, b, 0);
        ZT.Wire(graph, source, 0, c, 0);

        Assert.True(_engine.Run(graph).Success);
        var firstRun = log.ToArray();
        Assert.Equal(3, firstRun.Length);

        // Re-dirty everything and run again: the arbitrary order must repeat.
        log.Clear();
        a.Touch();
        b.Touch();
        c.Touch();
        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(firstRun, log);
    }

    [Fact]
    public void DataDependencies_AlwaysDecideTheOrder()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);

        // The consumer is CREATED before its producer, so any creation-order
        // tie-break would prefer it — the wire must win regardless.
        var consumer = new RecordingNode(log, "C");
        var producer = new RecordingNode(log, "P");
        graph.AddNode(consumer);
        graph.AddNode(producer);
        ZT.Wire(graph, source, 0, producer, 0);
        ZT.Wire(graph, producer, 0, consumer, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new[] { "P", "C" }, log);
    }

    [Fact]
    public void LoopBody_IndependentSideEffects_KeepTheSameOrderEveryIteration()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "x", "y", "z" });

        var loop = new Dyncamelo.Core.Nodes.LoopItemNode();
        graph.AddNode(loop);
        ZT.Wire(graph, items, 0, loop, 0);

        var a = new RecordingNode(log, "A");
        var b = new RecordingNode(log, "B");
        graph.AddNode(a);
        graph.AddNode(b);
        ZT.Wire(graph, loop, 0, a, 0);
        ZT.Wire(graph, loop, 0, b, 0);

        var collect = new Dyncamelo.Core.Nodes.LoopCollectNode();
        graph.AddNode(collect);
        ZT.Wire(graph, loop, 3, collect, 0);
        ZT.Wire(graph, a, 0, collect, 1);

        Assert.True(_engine.Run(graph).Success);

        // Whichever way the tie falls, every iteration must fall the same way.
        Assert.Equal(6, log.Count);
        var pattern = new[] { log[0], log[1] };
        Assert.Equal(new[] { pattern[0], pattern[1], pattern[0], pattern[1], pattern[0], pattern[1] }, log);
    }
}
