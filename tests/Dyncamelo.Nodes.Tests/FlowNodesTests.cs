using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

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
        Assert.Equal("payload", FlowNodes.Then("payload", after: "ignored", after2: 1, after3: 2));
        Assert.Null(FlowNodes.Then(null, after: "x"));
    }

    [Fact]
    public void Then_IsRegistered_WithOptionalExtraAfters()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);

        var definition = registry.Definitions.Single(d => d.Name == "Flow.Then");
        Assert.Equal("Dyncamelo.Nodes.FlowNodes.Then@object,object,object,object", definition.Id);
        Assert.Equal("Workflow", definition.Category);
        Assert.Equal(new[] { "value", "after", "after2", "after3" }, definition.Inputs.Select(i => i.Name));
        Assert.False(definition.Inputs[0].HasDefault);
        Assert.False(definition.Inputs[1].HasDefault);
        Assert.True(definition.Inputs[2].HasDefault);
        Assert.True(definition.Inputs[3].HasDefault);
        Assert.NotEmpty(definition.Inputs[1].Description);
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
        var then = registry.CreateZeroTouchNode("Dyncamelo.Nodes.FlowNodes.Then@object,object,object,object")!;
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
