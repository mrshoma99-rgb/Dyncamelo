using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Workflow;
using Xunit;

namespace CamelGraph.Nodes.Tests;

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

    // ----------------------------------------------------------------------------------------------------------------
    // Audit SYS-07 / SYS-08 / SYS-22: stop between actions, a failing item does not take the others down, actions wire in directly.
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Runs a body for the item and collects a tag; lets a test make one item fail or press Stop.</summary>
    private sealed class ScriptedAction : IWorkflowAction
    {
        private readonly List<string> _log;
        private readonly string _tag;
        private readonly Action<WorkflowContext>? _body;

        public ScriptedAction(List<string> log, string tag, Action<WorkflowContext>? body = null)
        {
            _log = log;
            _tag = tag;
            _body = body;
        }

        public string Describe() => _tag + " {name}";

        public void Run(WorkflowContext context)
        {
            _body?.Invoke(context);
            _log.Add(_tag + ":" + context.ItemName);
            context.Collect(_tag + "#" + context.Index);
        }
    }

    private static (GraphModel Graph, ZeroTouchNodeModel ForEach) BuildForEach(object? items, params object?[] actionValues)
    {
        var graph = new GraphModel();
        var itemsNode = new ConstNode(items);
        var forEach = ForEachNode();
        graph.AddNode(itemsNode);
        graph.AddNode(forEach);
        Assert.True(graph.Connect(itemsNode.OutPorts[0], forEach.InPorts[0]).Success);
        foreach (var action in actionValues)
        {
            var source = new ConstNode(action);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], forEach.InPorts[1]).Success);
        }

        return (graph, forEach);
    }

    [Fact]
    public void ForEach_TheOldTwoInputIdStillLoadsAndRuns()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var old = "CamelGraph.Nodes.WorkflowNodes.ForEach@System.Collections.Generic.IEnumerable<object>,System.Collections.Generic.IEnumerable<object>";

        var node = registry.CreateZeroTouchNode(old);

        Assert.NotNull(node);
        Assert.Equal("Workflow.ForEach", node!.Name);
        var log = new List<string>();
        var graph = new GraphModel();
        var items = new ConstNode(new List<object?> { "a", "b" });
        var actions = new ConstNode(new List<object?> { new ScriptedAction(log, "Save") });
        graph.AddNode(items);
        graph.AddNode(actions);
        graph.AddNode(node);
        Assert.True(graph.Connect(items.OutPorts[0], node.InPorts[0]).Success);
        Assert.True(graph.Connect(actions.OutPorts[0], node.InPorts[1]).Success);

        Assert.True(new GraphEngine().Run(graph).Success);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new[] { "Save:a", "Save:b" }, log);
    }

    [Fact]
    public void ForEach_ActionsTakeSeveralWiresInWireOrder()
    {
        var log = new List<string>();
        var (graph, forEach) = BuildForEach(
            new List<object?> { "a", "b" },
            new ScriptedAction(log, "Isolate"),
            new ScriptedAction(log, "Zoom"),
            new ScriptedAction(log, "Save"));

        Assert.True(forEach.InPorts[1].IsMultiInput);
        Assert.True(new GraphEngine().Run(graph).Success);

        Assert.Equal(NodeState.Executed, forEach.State);
        Assert.Equal(
            new[] { "Isolate:a", "Zoom:a", "Save:a", "Isolate:b", "Zoom:b", "Save:b" },
            log);
    }

    [Fact]
    public void ForEach_ASingleActionWiredStraightInIsOneAction()
    {
        var log = new List<string>();
        var (graph, forEach) = BuildForEach(new List<object?> { "a" }, new ScriptedAction(log, "Save"));

        Assert.True(new GraphEngine().Run(graph).Success);

        Assert.Equal(NodeState.Executed, forEach.State);
        Assert.Equal(new[] { "Save:a" }, log);
    }

    [Fact]
    public void ForEach_AFailingItemGivesAnEmptyResultAndTheOthersCarryOn()
    {
        var log = new List<string>();
        var save = new ScriptedAction(log, "Save", c =>
        {
            if (c.Index == 1)
            {
                throw new InvalidOperationException("the view could not be saved");
            }
        });
        var (graph, forEach) = BuildForEach(new List<object?> { "wall", "door", "slab" }, save);

        Assert.True(new GraphEngine().Run(graph).Success);

        // Item 2 failed: its slot is empty, items 1 and 3 are done.
        var results = Assert.IsAssignableFrom<IEnumerable<object?>>(forEach.OutPorts[0].Value).ToArray();
        Assert.Equal(new object?[] { "Save#0", null, "Save#2" }, results);
        Assert.Equal(new[] { "Save:wall", "Save:slab" }, log);

        // The node is amber and one line names the item and the action.
        Assert.Equal(NodeState.Warning, forEach.State);
        var message = Assert.Single(forEach.Messages).Text;
        Assert.Contains("1 of 3 item(s) failed", message);
        Assert.Contains("item 2 ('door')", message);
        Assert.Contains("action 'Save {name}'", message);
        Assert.Contains("the view could not be saved", message);
    }

    [Fact]
    public void ForEach_AnActionThatFailsMidItemSkipsTheRestOfThatItemOnly()
    {
        var log = new List<string>();
        var isolate = new ScriptedAction(log, "Isolate");
        var zoom = new ScriptedAction(log, "Zoom", c =>
        {
            if (c.Index == 0)
            {
                throw new InvalidOperationException("no view");
            }
        });
        var save = new ScriptedAction(log, "Save");
        var (graph, forEach) = BuildForEach(new List<object?> { "a", "b" }, isolate, zoom, save);

        new GraphEngine().Run(graph);

        // Item a: Isolate ran, Zoom failed, Save never ran. Item b ran in full.
        Assert.Equal(new[] { "Isolate:a", "Isolate:b", "Zoom:b", "Save:b" }, log);
        Assert.Contains("action 'Zoom {name}'", forEach.Messages.Single().Text);
        Assert.Contains("stay applied", forEach.Messages.Single().Text);
    }

    [Fact]
    public void ForEach_OnErrorStopEndsAtTheFirstFailureAndNamesItemAndAction()
    {
        var log = new List<string>();
        var save = new ScriptedAction(log, "Save", c =>
        {
            if (c.Index == 1)
            {
                throw new InvalidOperationException("boom");
            }
        });
        var (graph, forEach) = BuildForEach(new List<object?> { "wall", "door", "slab" }, save);
        forEach.InPorts[2].SetUserValue("stop");

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, forEach.State);
        Assert.Equal(new[] { "Save:wall" }, log);
        var text = forEach.StateMessage;
        Assert.Contains("item 2 ('door')", text);
        Assert.Contains("action 'Save {name}'", text);
        Assert.Contains("boom", text);
        Assert.Contains("1 earlier item(s) are done", text);
        Assert.Contains("onError", text);
    }

    [Fact]
    public void ForEach_OnErrorIsAnAdvancedChoiceAndAnythingElseIsRefused()
    {
        var definition = ForEachNode().Definition;
        var port = definition.Inputs.Single(i => i.Name == "onError");
        Assert.Equal("Advanced", port.Panel);
        Assert.Equal(new[] { "continue", "stop" }, port.Choices);
        Assert.Equal("continue", port.DefaultValue);

        var ex = Assert.Throws<ArgumentException>(() => WorkflowNodes.ForEach(new object?[] { "a" }, new object?[0], "sometimes"));
        Assert.Contains("'continue' or 'stop'", ex.Message);
    }

    [Fact]
    public void ForEach_ElementsThatAreNotActionsAreSkippedWithAWarning()
    {
        var log = new List<string>();
        var (graph, forEach) = BuildForEach(
            new List<object?> { "a" },
            new List<object?> { "not an action", null, new ScriptedAction(log, "Real"), 42 });

        Assert.True(new GraphEngine().Run(graph).Success);

        Assert.Equal(new[] { "Real:a" }, log);
        Assert.Equal(NodeState.Warning, forEach.State);
        var message = Assert.Single(forEach.Messages).Text;
        Assert.Contains("skipped 3 element(s) of 'actions' that are not actions", message);
        Assert.Contains("element 1 is the text \"not an action\"", message);
        Assert.Contains("element 2 is empty", message);
        Assert.DoesNotContain("No action is left", message);
    }

    [Fact]
    public void ForEach_WhenNoElementIsAnActionItSaysTheItemsComeBackUnchanged()
    {
        var (graph, forEach) = BuildForEach(new List<object?> { "a", "b" }, new List<object?> { "x" });

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Warning, forEach.State);
        Assert.Contains("No action is left, so the items come back unchanged.", forEach.Messages.Single().Text);
        Assert.Equal(new object?[] { "a", "b" }, Assert.IsAssignableFrom<IEnumerable<object?>>(forEach.OutPorts[0].Value).ToArray());
    }

    [Fact]
    public void ForEach_StopsBetweenTwoItemsWhenTheRunIsCancelled()
    {
        using var source = new CancellationTokenSource();
        var log = new List<string>();
        var save = new ScriptedAction(log, "Save", c =>
        {
            // The user presses Stop while item 2 is being saved.
            if (c.Index == 1)
            {
                source.Cancel();
            }
        });
        var (graph, forEach) = BuildForEach(new List<object?> { "a", "b", "c", "d" }, save);

        var result = new GraphEngine().Run(graph, new EvaluationContext(source.Token));

        Assert.True(result.Cancelled);
        // Items a and b were done; c and d never started.
        Assert.Equal(new[] { "Save:a", "Save:b" }, log);
        Assert.NotEqual(NodeState.Error, forEach.State);
    }

    [Fact]
    public void ForEach_StopsBetweenTwoActionsOfOneItem()
    {
        using var source = new CancellationTokenSource();
        var log = new List<string>();
        var isolate = new ScriptedAction(log, "Isolate", c => source.Cancel());
        var save = new ScriptedAction(log, "Save");
        var (graph, forEach) = BuildForEach(new List<object?> { "a", "b" }, isolate, save);

        var result = new GraphEngine().Run(graph, new EvaluationContext(source.Token));

        Assert.True(result.Cancelled);
        Assert.Equal(new[] { "Isolate:a" }, log);
    }

    [Fact]
    public void ForEach_ACancelInsideAnActionIsNotTurnedIntoAFailedItem()
    {
        using var source = new CancellationTokenSource();
        var log = new List<string>();
        var save = new ScriptedAction(log, "Save", c =>
        {
            source.Cancel();
            source.Token.ThrowIfCancellationRequested();
        });
        var (graph, forEach) = BuildForEach(new List<object?> { "a", "b" }, save);

        var result = new GraphEngine().Run(graph, new EvaluationContext(source.Token));

        Assert.True(result.Cancelled);
        Assert.Empty(log);
        Assert.NotEqual(NodeState.Warning, forEach.State);
    }

    [Fact]
    public void ForEach_DescriptionSaysWhenToUseItAndWhenToUseTheLoop()
    {
        var description = ForEachNode().Definition.Description;

        Assert.Contains("Loop.Item and Loop.Collect", description);
        Assert.Contains("onError", description);
        Assert.Contains("'results' has one entry per item", description);
    }
}
