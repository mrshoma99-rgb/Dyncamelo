using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Runs one library node under the real engine with values (lists included) wired to its first inputs.</summary>
internal static class EngineRun
{
    /// <summary>A node that only hands out the value it was given, to feed a list or a null into a graph.</summary>
    internal sealed class ValueSource : NodeModel
    {
        private readonly object? _value;

        public ValueSource(object? value)
        {
            _value = value;
            Name = "Value";
            AddOutput("value", typeof(object));
        }

        public override string NodeType => "TestValueSource";

        public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { _value };
    }

    internal static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    internal static ZeroTouchNodeModel Create(NodeRegistry registry, string name) =>
        new ZeroTouchNodeModel(registry.Definitions.Single(d => d.Name == name));

    /// <summary>
    /// Runs the node named <paramref name="name"/> with <paramref name="values"/> on its first inputs (in order). A port index
    /// of 0 or more in <paramref name="levelOnPort"/> sets List Levels @L1 on that input first; -1 leaves the levels alone.
    /// </summary>
    internal static ZeroTouchNodeModel Run(string name, int levelOnPort, params object?[] values)
    {
        var registry = Registry();
        var graph = new GraphModel();
        var node = Create(registry, name);
        graph.AddNode(node);
        for (var i = 0; i < values.Length; i++)
        {
            var source = new ValueSource(values[i]);
            graph.AddNode(source);
            Assert.True(graph.Connect(source.OutPorts[0], node.InPorts[i]).Success);
        }

        if (levelOnPort >= 0)
        {
            node.InPorts[levelOnPort].SetLevels(true, 1, false);
        }

        new GraphEngine().Run(graph);
        return node;
    }
}
