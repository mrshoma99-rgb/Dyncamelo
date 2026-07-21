using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Tests.Fixtures;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Nodes whose data dependencies leave their order open (typically parallel
/// side-effect branches) must run in CANVAS order — top-to-bottom, then
/// left-to-right — not in the invisible order they were added to the file.
/// Creation order reshuffles on every edit/copy/paste, which made loops of
/// write nodes execute in a different order after unrelated changes.
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
            AddInput("in", typeof(object));
            AddOutput("out", typeof(object));
        }

        public override string NodeType => "TestRecording";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            _log.Add(_tag);
            return new object?[] { inputs[0] };
        }
    }

    private static RecordingNode Add(GraphModel graph, List<string> log, string tag, double x, double y)
    {
        var node = new RecordingNode(log, tag) { X = x, Y = y };
        graph.AddNode(node);
        return node;
    }

    [Fact]
    public void IndependentBranches_RunTopToBottom_NotInCreationOrder()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);

        // A is created FIRST but placed BELOW B: canvas order must win.
        var a = Add(graph, log, "A", x: 100, y: 200);
        var b = Add(graph, log, "B", x: 100, y: 0);
        ZT.Wire(graph, source, 0, a, 0);
        ZT.Wire(graph, source, 0, b, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new[] { "B", "A" }, log);
    }

    [Fact]
    public void IndependentBranches_SameRow_RunLeftToRight()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);

        var right = Add(graph, log, "R", x: 500, y: 50);
        var left = Add(graph, log, "L", x: 100, y: 50);
        ZT.Wire(graph, source, 0, right, 0);
        ZT.Wire(graph, source, 0, left, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new[] { "L", "R" }, log);
    }

    [Fact]
    public void DataDependencies_StillBeatCanvasPosition()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var source = ZT.Value(graph, 1.0);

        // Downstream sits ABOVE its producer — dependency order must hold anyway.
        var producer = Add(graph, log, "P", x: 100, y: 300);
        var consumer = Add(graph, log, "C", x: 100, y: 0);
        ZT.Wire(graph, source, 0, producer, 0);
        ZT.Wire(graph, producer, 0, consumer, 0);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new[] { "P", "C" }, log);
    }

    [Fact]
    public void LoopBody_IndependentSideEffects_RunInCanvasOrder_EveryIteration()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "x", "y" });

        var loop = new Dyncamelo.Core.Nodes.LoopItemNode();
        graph.AddNode(loop);
        ZT.Wire(graph, items, 0, loop, 0);

        // "Save" is created BEFORE "Section" (older creation index) but placed
        // BELOW it — mirroring the flaky isolated-viewpoints graph. Canvas
        // order must make Section run first in every iteration.
        var save = Add(graph, log, "Save", x: 900, y: 200);
        var section = Add(graph, log, "Section", x: 900, y: -100);
        ZT.Wire(graph, loop, 0, save, 0);    // item -> Save
        ZT.Wire(graph, loop, 0, section, 0); // item -> Section

        var collect = new Dyncamelo.Core.Nodes.LoopCollectNode();
        graph.AddNode(collect);
        ZT.Wire(graph, loop, 3, collect, 0);
        ZT.Wire(graph, save, 0, collect, 1);

        Assert.True(_engine.Run(graph).Success);
        Assert.Equal(new[] { "Section", "Save", "Section", "Save" }, log);
    }
}
