using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Tests;

/// <summary>A node that hands out whatever value a test gives it, so a list (with gaps) can be wired into the node under test.</summary>
public sealed class Val2ValueSource : NodeModel
{
    private readonly object? _value;

    public Val2ValueSource(object? value)
    {
        _value = value;
        Name = "Value";
        AddOutput("value", typeof(object));
    }

    public override string NodeType => "Val2Value";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { _value };
}

/// <summary>Runs one library node through the real engine with the given values on its first inputs.</summary>
public static class Val2Harness
{
    public static NodeRegistry Registry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    public static List<object?> L(params object?[] items) => new List<object?>(items);

    /// <summary>Creates a node by its catalogue name or by any id (current or an alias of an older signature).</summary>
    public static ZeroTouchNodeModel Create(NodeRegistry registry, string nameOrId)
    {
        var byName = registry.Definitions.FirstOrDefault(d => d.Name == nameOrId && !d.IsDeprecated);
        return byName != null
            ? new ZeroTouchNodeModel(byName)
            : registry.CreateZeroTouchNode(nameOrId) ?? throw new System.InvalidOperationException("No node '" + nameOrId + "'.");
    }

    /// <summary>Wires <paramref name="values"/> to the first inputs (null entries leave the input unwired), runs the graph and returns the node.</summary>
    public static ZeroTouchNodeModel Run(ZeroTouchNodeModel node, params object?[] values)
    {
        var graph = new GraphModel();
        graph.AddNode(node);
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is Unset)
            {
                continue;
            }

            var source = new Val2ValueSource(values[i]);
            graph.AddNode(source);
            var result = graph.Connect(source.OutPorts[0], node.InPorts[i]);
            if (!result.Success)
            {
                throw new System.InvalidOperationException("Wiring failed: " + result.Message);
            }
        }

        new GraphEngine().Run(graph);
        return node;
    }

    /// <summary>Marks an input that stays unwired (its typed-in or default value is used).</summary>
    public sealed class Unset
    {
        public static readonly Unset Value = new Unset();
    }
}
