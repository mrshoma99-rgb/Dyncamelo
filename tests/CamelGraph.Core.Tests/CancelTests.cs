using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Tests.Fixtures;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>
/// Cancelling a run: the host gets a heartbeat before every node, every replicated call and every loop pass, may cancel
/// from inside it, and whatever was interrupted stays dirty so the next run resumes exactly there.
/// </summary>
public class CancelTests
{
    private readonly GraphEngine _engine = new GraphEngine();

    private sealed class RecordingNode : NodeModel
    {
        private readonly List<string> _log;

        public RecordingNode(List<string> log, string name)
        {
            _log = log;
            Name = name;
            AddInput("in", typeof(object));
            AddOutput("out", typeof(object));
        }

        public override string NodeType => "TestRecordingCancel";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context)
        {
            _log.Add(Name + ":" + (inputs[0]?.ToString() ?? "null"));
            return new object?[] { inputs[0] };
        }
    }

    [Fact]
    public void ProgressIsReportedBeforeEachNodeWithTheTotal()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 16.0);
        var b = ZT.Node("Sqrt");
        var c = ZT.Node("AddStep");
        graph.AddNode(b);
        graph.AddNode(c);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, b, 0, c, 0);

        var seen = new List<RunProgress>();
        var context = new EvaluationContext { ProgressCallback = seen.Add };
        var result = _engine.Run(graph, context);

        Assert.Equal(3, result.PlannedCount);
        Assert.Equal(new[] { 0, 1, 2 }, seen.Select(p => p.Completed).ToArray());
        Assert.All(seen, p => Assert.Equal(3, p.Total));
        Assert.Equal("Square Root", seen[1].Node);
        Assert.Equal("2 / 3 — Square Root", seen[1].Describe());
        Assert.Empty(seen[0].Scope);
    }

    [Fact]
    public void OnlyNodesThatWillRunAreCounted()
    {
        var graph = new GraphModel();
        var a = ZT.Value(graph, 16.0);
        var b = ZT.Node("Sqrt");
        graph.AddNode(b);
        ZT.Wire(graph, a, 0, b, 0);
        _engine.Run(graph);

        b.MarkDirty(); // only b (and nothing downstream) is dirty now
        var seen = new List<RunProgress>();
        var result = _engine.Run(graph, new EvaluationContext { ProgressCallback = seen.Add });

        Assert.Equal(1, result.PlannedCount);
        Assert.Single(seen);
    }

    [Fact]
    public void AHeartbeatThatCancelsStopsBeforeTheNextNodeAndTheRunResumes()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var a = ZT.Value(graph, "x");
        var b = new RecordingNode(log, "B");
        var c = new RecordingNode(log, "C");
        graph.AddNode(b);
        graph.AddNode(c);
        ZT.Wire(graph, a, 0, b, 0);
        ZT.Wire(graph, b, 0, c, 0);

        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        context.ProgressCallback = p =>
        {
            if (p.Node == "C")
            {
                source.Cancel(); // the user pressed Esc just before C
            }
        };

        var result = _engine.Run(graph, context);

        Assert.True(result.Cancelled);
        Assert.Equal(new[] { "B:x" }, log.ToArray());
        Assert.Equal(2, result.ExecutedNodes.Count);   // the value node and B
        Assert.False(b.IsDirty);
        Assert.True(c.IsDirty);

        var resumed = _engine.Run(graph);
        Assert.True(resumed.Success);
        Assert.Equal(new[] { "B:x", "C:x" }, log.ToArray());   // B did not run again
        Assert.Single(resumed.ExecutedNodes);
    }

    [Fact]
    public void ACancelBetweenReplicatedCallsLeavesTheNodeDirtyWithItsOldOutputs()
    {
        var graph = new GraphModel();
        var list = ZT.Value(graph, new List<object?> { 1.0, 4.0, 9.0, 16.0, 25.0 });
        var sqrt = ZT.Node("Sqrt");
        graph.AddNode(sqrt);
        ZT.Wire(graph, list, 0, sqrt, 0);

        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        var inSqrt = false;
        var beats = 0;
        context.ProgressCallback = p => inSqrt = p.Node == "Square Root";
        context.Heartbeat = () =>
        {
            if (inSqrt && ++beats == 4)
            {
                source.Cancel();   // one beat for the node, then three replicated calls
            }
        };

        var result = _engine.Run(graph, context);

        Assert.True(result.Cancelled);
        Assert.True(sqrt.IsDirty);
        Assert.Null(sqrt.OutPorts[0].Value);            // the half-finished list was never published
        Assert.NotEqual(NodeState.Error, sqrt.State);   // a cancel is not a failure
        Assert.DoesNotContain(sqrt.Messages, m => m.Severity >= MessageSeverity.Warning);

        var resumed = _engine.Run(graph);
        Assert.True(resumed.Success);
        Assert.Equal(new object?[] { 1.0, 2.0, 3.0, 4.0, 5.0 }, ((IEnumerable<object?>)sqrt.OutPorts[0].Value!).ToArray());
    }

    [Fact]
    public void ACancelledLoopPublishesNothingAndStaysDirty()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var items = ZT.Value(graph, new List<object?> { "a", "b", "c", "d" });
        var loop = new LoopItemNode();
        graph.AddNode(loop);
        ZT.Wire(graph, items, 0, loop, 0);
        var body = new RecordingNode(log, "Body");
        graph.AddNode(body);
        ZT.Wire(graph, loop, 0, body, 0);
        var collect = new LoopCollectNode();
        graph.AddNode(collect);
        ZT.Wire(graph, loop, 3, collect, 0);
        ZT.Wire(graph, body, 0, collect, 1);

        using var source = new CancellationTokenSource();
        var context = new EvaluationContext(source.Token);
        context.Heartbeat = () =>
        {
            if (log.Count == 2)
            {
                source.Cancel();   // after two passes
            }
        };

        var result = _engine.Run(graph, context);

        Assert.True(result.Cancelled);
        Assert.Equal(new[] { "Body:a", "Body:b" }, log.ToArray());
        Assert.True(collect.IsDirty);
        Assert.True(body.IsDirty);
        Assert.Null(collect.OutPorts[0].Value);   // no half-collected list

        log.Clear();
        var resumed = _engine.Run(graph);
        Assert.True(resumed.Success);
        Assert.Equal(new[] { "Body:a", "Body:b", "Body:c", "Body:d" }, log.ToArray());
        Assert.Equal(4, ((IList<object?>)collect.OutPorts[0].Value!).Count);
    }

    [Fact]
    public void WithNothingDirtyACancelledTokenIsNotACancelledRun()
    {
        var graph = new GraphModel();
        ZT.Value(graph, 1.0);
        _engine.Run(graph);

        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = _engine.Run(graph, new EvaluationContext(source.Token));

        Assert.False(result.Cancelled);
        Assert.Equal(0, result.PlannedCount);
    }

    [Fact]
    public void TheContextTakesItsTokenLaterAndTracksTheGroupScope()
    {
        var context = new EvaluationContext();
        using var source = new CancellationTokenSource();
        context.UseCancellation(source.Token);
        source.Cancel();
        Assert.Throws<System.OperationCanceledException>(() => context.Checkpoint());

        var seen = new List<RunProgress>();
        var fresh = new EvaluationContext { ProgressCallback = seen.Add };
        using (fresh.EnterScope("Outer"))
        using (fresh.EnterScope("Inner"))
        {
            fresh.ReportProgress(2, 5, "Node");
        }

        fresh.ReportProgress(0, 1, "After");
        Assert.Equal("3 / 5 — Outer ▸ Inner ▸ Node", seen[0].Describe());
        Assert.Empty(seen[1].Scope);
    }
}
