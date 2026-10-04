using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>A node that delivers one fixed value; feeds lists and dictionaries into the node under test.</summary>
internal sealed class FixedValueNode : NodeModel
{
    private readonly object? _value;

    public FixedValueNode(object? value)
    {
        _value = value;
        Name = "Value";
        AddOutput("value", typeof(object));
    }

    public override string NodeType => "TestFixedValue";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { _value };
}

/// <summary>
/// Runs one library node under the real engine (replication, warnings, error states) with every input wired from a fixed value.
/// Use <see cref="Unwired"/> for an input that should keep its default.
/// </summary>
internal static class NodeRun
{
    /// <summary>Marks an input that stays unwired.</summary>
    public static readonly object Unwired = new object();

    private static readonly NodeRegistry SharedRegistry = Create();

    /// <summary>The default registry plus the whole node library.</summary>
    public static NodeRegistry Registry => SharedRegistry;

    private static NodeRegistry Create()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    /// <summary>The definition of the node with this display name.</summary>
    public static NodeDefinition Definition(string name) => SharedRegistry.Definitions.Single(d => d.Name == name);

    /// <summary>Runs the node with this display name.</summary>
    public static ZeroTouchNodeModel Run(string name, params object?[] inputs) => RunDefinition(Definition(name), inputs);

    /// <summary>Runs the node whose definition id (current or old alias) is given.</summary>
    public static ZeroTouchNodeModel RunId(string id, params object?[] inputs)
    {
        Assert.True(SharedRegistry.TryGetDefinition(id, out var definition), "unknown definition id " + id);
        return RunDefinition(definition!, inputs);
    }

    private static ZeroTouchNodeModel RunDefinition(NodeDefinition definition, object?[] inputs)
    {
        var graph = new GraphModel();
        var node = new ZeroTouchNodeModel(definition);
        graph.AddNode(node);
        for (var i = 0; i < inputs.Length; i++)
        {
            if (ReferenceEquals(inputs[i], Unwired))
            {
                continue;
            }

            var source = new FixedValueNode(inputs[i]);
            graph.AddNode(source);
            var result = graph.Connect(source.OutPorts[0], node.InPorts[i]);
            Assert.True(result.Success, result.Message);
        }

        new GraphEngine().Run(graph);
        return node;
    }

    /// <summary>
    /// Saves a graph holding one library node (with some typed-in values) as a file written by an older release would have it
    /// (its definition id and port names replaced by the old ones), loads that file again, wires the given values into the
    /// loaded node and runs it.
    /// </summary>
    /// <param name="name">Display name of the node.</param>
    /// <param name="oldId">The definition id the old file carries.</param>
    /// <param name="renamedPorts">Ports that carried another name in the old file.</param>
    /// <param name="typed">Values typed into input ports (port index, value) before the file is written.</param>
    /// <param name="wired">Values wired into input ports (port index, value) after the file is loaded.</param>
    public static ZeroTouchNodeModel RunSavedAs(
        string name,
        string oldId,
        IEnumerable<(string Port, string OldPort)> renamedPorts,
        IEnumerable<(int Port, object? Value)> typed,
        IEnumerable<(int Port, object? Value)> wired)
    {
        var definition = Definition(name);
        var graph = new GraphModel();
        var node = new ZeroTouchNodeModel(definition);
        graph.AddNode(node);
        foreach (var (port, value) in typed)
        {
            node.InPorts[port].SetUserValue(value);
        }

        var serializer = new GraphSerializer(SharedRegistry);
        var json = serializer.Serialize(graph);
        Assert.Contains(definition.Id, json);
        json = json.Replace(definition.Id, oldId);
        foreach (var (port, oldPort) in renamedPorts)
        {
            json = json.Replace("\"" + port + "\"", "\"" + oldPort + "\"");
        }

        var loadedSerializer = new GraphSerializer(SharedRegistry);
        var loaded = loadedSerializer.Deserialize(json);
        Assert.Empty(loadedSerializer.LoadWarnings);
        var loadedNode = loaded.Nodes.OfType<ZeroTouchNodeModel>().Single(n => n.Definition.Id == definition.Id);
        foreach (var (port, value) in wired)
        {
            var source = new FixedValueNode(value);
            loaded.AddNode(source);
            var result = loaded.Connect(source.OutPorts[0], loadedNode.InPorts[port]);
            Assert.True(result.Success, result.Message);
        }

        new GraphEngine().Run(loaded);
        return loadedNode;
    }

    public static List<object?> L(params object?[] items) => new List<object?>(items);

    public static IList<object?> Out(ZeroTouchNodeModel node, int port = 0) =>
        Assert.IsAssignableFrom<IList<object?>>(node.OutPorts[port].Value);
}
