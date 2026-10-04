using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Flow.Then — the explicit ordering node: it passes its value through
/// unchanged but only becomes ready after the wired 'after' nodes ran,
/// turning "B must run after A" into a real data dependency.
/// </summary>
public class FlowNodesTests
{
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
    }

    [Fact]
    public void Then_PassesValueThrough_IgnoringAfters()
    {
        Assert.Equal("payload", FlowNodes.Then("payload", new object?[] { "ignored", 1, 2 }));
        Assert.Null(FlowNodes.Then(null, new object?[] { "x" }));
    }

    [Fact]
    public void Then_IsRegistered_WithOneMultiInputAfter()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var definition = registry.Definitions.Single(d => d.Name == "Flow.Then");
        Assert.Equal("CamelGraph.Nodes.FlowNodes.Then@object,System.Collections.Generic.IEnumerable<object>", definition.Id);
        Assert.Equal("Workflow", definition.Category);
        Assert.Equal(new[] { "value", "after" }, definition.Inputs.Select(i => i.Name));
        Assert.False(definition.Inputs[0].HasDefault);
        Assert.False(definition.Inputs[1].HasDefault);
        Assert.True(definition.Inputs[1].MultiInput);
        Assert.Equal(new[] { "after2", "after3" }, definition.Inputs[1].Aliases);
        Assert.NotEmpty(definition.Inputs[1].Description);
        Assert.Contains("CamelGraph.Nodes.FlowNodes.Then@object,object,object,object", definition.Aliases);
    }

    private static ZeroTouchNodeModel Then(NodeRegistry registry) =>
        registry.CreateZeroTouchNode(registry.Definitions.Single(d => d.Name == "Flow.Then").Id)!;

    private static ZeroTouchNodeModel Require(NodeRegistry registry) =>
        registry.CreateZeroTouchNode(registry.Definitions.Single(d => d.Name == "Flow.Require").Id)!;

    [Fact]
    public void Then_WaitsForEveryWireIntoAfter_InAnyCreationOrder()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var log = new List<string>();
        var graph = new GraphModel();

        var source = new RecordingNode(log, "Source");
        graph.AddNode(source);
        var consumer = new RecordingNode(log, "Save");
        graph.AddNode(consumer);
        var then = Then(registry);
        graph.AddNode(then);
        var steps = new[] { new RecordingNode(log, "SectionBox"), new RecordingNode(log, "Hide"), new RecordingNode(log, "Colour"), new RecordingNode(log, "Ghost") };
        foreach (var step in steps)
        {
            graph.AddNode(step);
            Assert.True(graph.Connect(source.OutPorts[0], step.InPorts[0]).Success);
            Assert.True(graph.Connect(step.OutPorts[0], then.InPorts[1]).Success);
        }

        Assert.True(graph.Connect(source.OutPorts[0], then.InPorts[0]).Success);
        Assert.True(graph.Connect(then.OutPorts[0], consumer.InPorts[0]).Success);

        Assert.True(new GraphEngine().Run(graph).Success);

        // Four prerequisites (the old node had room for three), then the save.
        Assert.Equal(6, log.Count);
        Assert.Equal("Save", log[log.Count - 1]);
        Assert.Equal(4, graph.FindConnectionsInto(then.InPorts[1]).Count);
    }

    [Fact]
    public void Then_ASavedGraphWithThreeAfterSocketsStillLoadsAndKeepsAllThreeWiresInOrder()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var then = Then(registry);
        graph.AddNode(then);
        var value = new CamelGraph.Core.Nodes.StringInputNode { Value = "payload" };
        graph.AddNode(value);
        var steps = new[] { new CamelGraph.Core.Nodes.StringInputNode { Name = "A", Value = "a" }, new CamelGraph.Core.Nodes.StringInputNode { Name = "B", Value = "b" }, new CamelGraph.Core.Nodes.StringInputNode { Name = "C", Value = "c" } };
        foreach (var step in steps)
        {
            graph.AddNode(step);
            Assert.True(graph.Connect(step.OutPorts[0], then.InPorts[1]).Success);
        }

        Assert.True(graph.Connect(value.OutPorts[0], then.InPorts[0]).Success);

        // Written by an earlier version: the old id, and the three wires into after, after2 and after3.
        var serializer = new GraphSerializer(registry);
        var json = serializer.Serialize(graph);
        json = json.Replace(
            "CamelGraph.Nodes.FlowNodes.Then@object,System.Collections.Generic.IEnumerable<object>",
            "CamelGraph.Nodes.FlowNodes.Then@object,object,object,object");
        const string Wire = "\"ToPort\": \"after\"";
        var second = json.IndexOf(Wire, json.IndexOf(Wire, StringComparison.Ordinal) + 1, StringComparison.Ordinal);
        var third = json.IndexOf(Wire, second + 1, StringComparison.Ordinal);
        Assert.True(third > 0, "expected three wires into after");
        json = json.Remove(third, Wire.Length).Insert(third, "\"ToPort\": \"after3\"");
        json = json.Remove(second, Wire.Length).Insert(second, "\"ToPort\": \"after2\"");

        var loaded = serializer.Deserialize(json);

        Assert.Empty(serializer.LoadWarnings);
        var loadedThen = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Name == "Flow.Then");
        Assert.Equal("CamelGraph.Nodes.FlowNodes.Then@object,System.Collections.Generic.IEnumerable<object>", loadedThen.Definition.Id);
        var wires = loaded.FindConnectionsInto(loadedThen.InPorts[1]);
        Assert.Equal(new[] { "A", "B", "C" }, wires.Select(w => w.SourceNode.Name));
        Assert.True(new GraphEngine().Run(loaded).Success);
        Assert.True(NodeState.Executed == loadedThen.State, string.Join(" / ", loadedThen.Messages.Select(m => m.Text)));
    }

    [Fact]
    public void Then_ASwitchedOffBranchCountsAsNothing_AndAllOffSkipsTheNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        Assert.Equal(NodeState.Executed, RunThen(registry, InactiveValue.Instance, "ran").State);
        Assert.Equal(NodeState.Idle, RunThen(registry, InactiveValue.Instance, InactiveValue.Instance).State);
    }

    [Fact]
    public void Then_AListOrANullOrAnythingElseOnASingleAfterWireStillOnlyOrdersTheNode()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        foreach (var after in new object?[] { new List<object?> { 1, 2, 3 }, null, "text", 42, new[] { "a", "b" }, new Dictionary<string, object> { ["k"] = 1 } })
        {
            var graph = new GraphModel();
            var then = Then(registry);
            graph.AddNode(then);
            var payload = new ConstNode(new List<object?> { "x", "y" });
            var prerequisite = new ConstNode(after);
            graph.AddNode(payload);
            graph.AddNode(prerequisite);
            Assert.True(graph.Connect(payload.OutPorts[0], then.InPorts[0]).Success);
            Assert.True(graph.Connect(prerequisite.OutPorts[0], then.InPorts[1]).Success);

            new GraphEngine().Run(graph);

            // One run (not one per element of 'after'), the value comes out whole, no warning.
            Assert.Equal(NodeState.Executed, then.State);
            Assert.Equal(new object?[] { "x", "y" }, Assert.IsAssignableFrom<IEnumerable<object?>>(then.OutPorts[0].Value).ToArray());
        }
    }

    private static ZeroTouchNodeModel RunThen(NodeRegistry registry, object? firstAfter, object? secondAfter)
    {
        var graph = new GraphModel();
        var then = Then(registry);
        graph.AddNode(then);
        var payload = new ConstNode("payload");
        graph.AddNode(payload);
        Assert.True(graph.Connect(payload.OutPorts[0], then.InPorts[0]).Success);
        foreach (var branch in new[] { firstAfter, secondAfter })
        {
            var source = new ConstNode(branch);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], then.InPorts[1]).Success);
        }

        new GraphEngine().Run(graph);
        return then;
    }

    private sealed class ConstNode : NodeModel
    {
        private readonly object? _value;

        public ConstNode(object? value)
        {
            _value = value;
            Name = "Const";
            AddOutput("value", typeof(object));
        }

        public override string NodeType => "TestConstFlow";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new[] { _value };
    }

    // -------------------------------------------------------------------------------------------------- Flow.Require

    [Fact]
    public void Require_TruePassesTheValueThrough()
    {
        Assert.Equal("payload", FlowNodes.Require("payload", true, "never shown"));
        Assert.Null(FlowNodes.Require(null, true));
    }

    [Fact]
    public void Require_FalseRaisesTheMessageAsItIs()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FlowNodes.Require("payload", false, "  The model is not saved. Save it first.  "));

        Assert.Equal("The model is not saved. Save it first.", ex.Message);
    }

    [Fact]
    public void Require_ABlankMessageStillSaysSomething()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FlowNodes.Require("payload", false, "   "));

        Assert.Equal("A required condition is false.", ex.Message);
    }

    [Fact]
    public void Require_IsRegisteredAsACreateNodeWithAMessage()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var definition = registry.Definitions.Single(d => d.Name == "Flow.Require");

        Assert.Equal("Workflow", definition.Category);
        Assert.Equal(CamelGraph.Core.Graph.NodeFunction.Create, definition.Function);
        Assert.Equal(new[] { "value", "condition", "message" }, definition.Inputs.Select(i => i.Name));
        Assert.True(definition.Inputs[2].HasDefault);
        Assert.Contains("require", definition.SearchTags);
        Assert.Contains("assert", definition.SearchTags);
    }

    [Fact]
    public void Require_AFalseConditionIsTheNodesErrorAndTheStepAfterItDoesNotRun()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var log = new List<string>();
        var graph = new GraphModel();
        var value = new ConstNode("payload");
        var condition = new ConstNode(false);
        var require = Require(registry);
        var destructive = new RecordingNode(log, "ReplaceModel");
        foreach (var node in new NodeModel[] { value, condition, require, destructive })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(value.OutPorts[0], require.InPorts[0]).Success);
        Assert.True(graph.Connect(condition.OutPorts[0], require.InPorts[1]).Success);
        Assert.True(graph.Connect(require.OutPorts[0], destructive.InPorts[0]).Success);
        require.InPorts[2].SetUserValue("The file list is empty. Pick the files first.");

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, require.State);
        Assert.Contains("The file list is empty. Pick the files first.", require.StateMessage);
        Assert.Empty(log);
        Assert.NotEqual(NodeState.Executed, destructive.State);
    }

    [Fact]
    public void Require_ATrueConditionLetsTheStepRun()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var log = new List<string>();
        var graph = new GraphModel();
        var value = new ConstNode("payload");
        var condition = new ConstNode(true);
        var require = Require(registry);
        var step = new RecordingNode(log, "Step");
        foreach (var node in new NodeModel[] { value, condition, require, step })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(value.OutPorts[0], require.InPorts[0]).Success);
        Assert.True(graph.Connect(condition.OutPorts[0], require.InPorts[1]).Success);
        Assert.True(graph.Connect(require.OutPorts[0], step.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, require.State);
        Assert.Equal(new[] { "Step" }, log);
    }

    [Fact]
    public void Require_FlowTryCanStillCatchTheDeliberateError()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        var graph = new GraphModel();
        var value = new ConstNode("payload");
        var condition = new ConstNode(false);
        var require = Require(registry);
        var attempt = registry.CreateZeroTouchNode(registry.Definitions.Single(d => d.Name == "Flow.Try").Id)!;
        foreach (var node in new NodeModel[] { value, condition, require, attempt })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(value.OutPorts[0], require.InPorts[0]).Success);
        Assert.True(graph.Connect(condition.OutPorts[0], require.InPorts[1]).Success);
        Assert.True(graph.Connect(require.OutPorts[0], attempt.InPorts[0]).Success);
        require.InPorts[2].SetUserValue("Nothing is selected.");

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, attempt.State);
        Assert.Equal(true, attempt.OutPorts[1].Value);
        Assert.Contains("Nothing is selected.", (string)attempt.OutPorts[2].Value!);
    }

    [Fact]
    public void Then_ForcesSideEffectOrder_AgainstTheEnginesTieBreak()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var log = new List<string>();
        var graph = new GraphModel();

        var source = new RecordingNode(log, "Source");
        graph.AddNode(source);

        // Adversarial creation order: the save-side chain (consumer, then the
        // Then node) is created BEFORE the prerequisite, so the engine's
        // creation-index tie-break alone would run the save first. The
        // Flow.Then 'after' wire must turn the ordering into a hard data
        // dependency that wins regardless.
        var consumer = new RecordingNode(log, "Save");
        graph.AddNode(consumer);
        var then = registry.CreateZeroTouchNode("CamelGraph.Nodes.FlowNodes.Then@object,object,object,object")!;
        graph.AddNode(then);
        var prerequisite = new RecordingNode(log, "Section");
        graph.AddNode(prerequisite);

        Assert.True(graph.Connect(source.OutPorts[0], prerequisite.InPorts[0]).Success);
        Assert.True(graph.Connect(source.OutPorts[0], then.InPorts[0]).Success);          // value
        Assert.True(graph.Connect(prerequisite.OutPorts[0], then.InPorts[1]).Success);    // after
        Assert.True(graph.Connect(then.OutPorts[0], consumer.InPorts[0]).Success);

        var engine = new GraphEngine();
        Assert.True(engine.Run(graph).Success);

        Assert.Equal(new[] { "Source", "Section", "Save" }, log);
    }
}
