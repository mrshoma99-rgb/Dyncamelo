using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Workflow;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Tests for Workflow.ForEach — the per-item ordered loop that sequences reified
/// actions one item at a time. The key guarantee (which lacing cannot provide)
/// is that every action for item N runs, in order, before any action for item N+1.
/// </summary>
public class WorkflowNodesTests
{
    /// <summary>Records the order in which it runs and collects a per-iteration value.</summary>
    private sealed class RecordingAction : IWorkflowAction
    {
        private readonly List<string> _log;
        private readonly string _tag;

        public RecordingAction(List<string> log, string tag)
        {
            _log = log;
            _tag = tag;
        }

        public string Describe() => _tag;

        public void Run(WorkflowContext context)
        {
            _log.Add(_tag + ":" + context.ItemName);
            context.Collect(_tag + "#" + context.Index);
        }
    }

    /// <summary>Emits a fixed object onto its output port (feeds arbitrary values into a graph).</summary>
    private sealed class ConstNode : NodeModel
    {
        private readonly object? _value;

        public ConstNode(object? value)
        {
            _value = value;
            Name = "Const";
            AddOutput("value", typeof(object));
        }

        public override string NodeType => "TestConst";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { _value };
    }

    private static ZeroTouchNodeModel ForEachNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var definition = registry.Definitions.Single(d => d.Name == "Workflow.ForEach");
        return new ZeroTouchNodeModel(definition);
    }

    [Fact]
    public void ForEach_RunsEveryActionForAnItem_BeforeMovingToTheNext()
    {
        var log = new List<string>();
        var isolate = new RecordingAction(log, "Isolate");
        var zoom = new RecordingAction(log, "Zoom");
        var save = new RecordingAction(log, "Save");

        WorkflowNodes.ForEach(
            new object?[] { "a", "b", "c" },
            new object?[] { isolate, zoom, save });

        // Item-major, action-order-within-item — the sequencing lacing cannot do.
        Assert.Equal(
            new[]
            {
                "Isolate:a", "Zoom:a", "Save:a",
                "Isolate:b", "Zoom:b", "Save:b",
                "Isolate:c", "Zoom:c", "Save:c",
            },
            log);
    }

    [Fact]
    public void ForEach_CollectsPerItemResults()
    {
        var log = new List<string>();
        var save = new RecordingAction(log, "Save");

        var results = WorkflowNodes.ForEach(new object?[] { "a", "b" }, new object?[] { save });

        // One entry per item; a single collected value is unwrapped.
        Assert.Equal(new object?[] { "Save#0", "Save#1" }, results);
    }

    [Fact]
    public void ForEach_MultipleCollectedValues_AreKeptAsAList()
    {
        var log = new List<string>();
        var a = new RecordingAction(log, "A");
        var b = new RecordingAction(log, "B");

        var results = WorkflowNodes.ForEach(new object?[] { "x" }, new object?[] { a, b });

        var first = Assert.IsAssignableFrom<IEnumerable<object?>>(results[0]);
        Assert.Equal(new object?[] { "A#0", "B#0" }, first.ToArray());
    }

    [Fact]
    public void ForEach_NoActionsCollected_PassesTheItemThrough()
    {
        var results = WorkflowNodes.ForEach(new object?[] { "solo" }, new object?[0]);
        Assert.Equal(new object?[] { "solo" }, results);
    }

    [Fact]
    public void ForEach_EmptyItems_YieldsEmptyResults()
    {
        var log = new List<string>();
        var results = WorkflowNodes.ForEach(new object?[0], new object?[] { new RecordingAction(log, "X") });
        Assert.Empty(results);
        Assert.Empty(log);
    }

    [Fact]
    public void ForEach_SkipsNonActionElements_WithoutThrowing()
    {
        var log = new List<string>();
        var real = new RecordingAction(log, "Real");

        var results = WorkflowNodes.ForEach(
            new object?[] { "a" },
            new object?[] { "not an action", 42, real });

        Assert.Equal(new[] { "Real:a" }, log);
        Assert.Equal(new object?[] { "Real#0" }, results);
    }

    [Fact]
    public void ForEach_RunsThroughTheEngine_WiredFromListCreate()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var items = new ConstNode(new List<object?> { "a", "b" });
        var isolate = new ConstNode(new RecordingAction(log, "Isolate"));
        var save = new ConstNode(new RecordingAction(log, "Save"));
        var actions = new ListCreateNode();
        var forEach = ForEachNode();

        foreach (var node in new NodeModel[] { items, isolate, save, actions, forEach })
        {
            graph.AddNode(node);
        }

        actions.AddItemPort(); // two action ports
        Assert.True(graph.Connect(isolate.OutPorts[0], actions.InPorts[0]).Success);
        Assert.True(graph.Connect(save.OutPorts[0], actions.InPorts[1]).Success);
        Assert.True(graph.Connect(items.OutPorts[0], forEach.InPorts[0]).Success);
        Assert.True(graph.Connect(actions.OutPorts[0], forEach.InPorts[1]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, forEach.State);
        // Whole list bound to the node (no replication): item-major ordering holds.
        Assert.Equal(new[] { "Isolate:a", "Save:a", "Isolate:b", "Save:b" }, log);
        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(forEach.OutPorts[0].Value);
        Assert.Equal(2, results.Count());
    }
}
