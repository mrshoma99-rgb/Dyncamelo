using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Serialization;
using Dyncamelo.Nodes;
using Xunit;

namespace Dyncamelo.Integration.Tests;

/// <summary>
/// Helpers that push every test through the full PUBLIC pipeline:
/// build a GraphModel -> serialize to .dyc JSON -> deserialize -> run.
/// Nothing here reaches into internals.
/// </summary>
internal static class Pipeline
{
    /// <summary>Registry with Core built-ins plus the whole Dyncamelo.Nodes library.</summary>
    public static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    /// <summary>Serializes the graph to .dyc JSON and loads it back — the round-trip every test goes through.</summary>
    public static GraphModel SaveLoad(GraphModel graph, NodeRegistry registry)
    {
        var serializer = new GraphSerializer(registry);
        return serializer.Deserialize(serializer.Serialize(graph));
    }

    /// <summary>Round-trips the graph through .dyc JSON, runs the reloaded copy and returns it.</summary>
    public static GraphModel SaveLoadAndRun(GraphModel graph, NodeRegistry registry, out RunResult result)
    {
        var reloaded = SaveLoad(graph, registry);
        result = new GraphEngine().Run(reloaded);
        return reloaded;
    }

    /// <summary>Instantiates a zero-touch node by its library display name (e.g. "Add", "String.Split").</summary>
    public static ZeroTouchNodeModel ZeroTouch(NodeRegistry registry, string definitionName, string? rename = null)
    {
        var definition = registry.Definitions.FirstOrDefault(d => d.Name == definitionName);
        Assert.True(definition != null, "Zero-touch definition '" + definitionName + "' is not registered.");
        var node = new ZeroTouchNodeModel(definition!);
        if (rename != null)
        {
            node.Name = rename;
        }

        return node;
    }

    /// <summary>Connects source.outPort -> target.inPort and asserts the connection was accepted.</summary>
    public static void Connect(GraphModel graph, NodeModel source, string outPort, NodeModel target, string inPort)
    {
        var from = source.OutPorts.FirstOrDefault(p => p.Name == outPort);
        Assert.True(from != null, "Node '" + source.Name + "' has no output port '" + outPort + "'.");
        var to = target.InPorts.FirstOrDefault(p => p.Name == inPort);
        Assert.True(to != null, "Node '" + target.Name + "' has no input port '" + inPort + "'.");

        var result = graph.Connect(from!, to!);
        Assert.True(result.Success, "Connect " + source.Name + "." + outPort + " -> " + target.Name + "." + inPort + " failed: " + result.Message);
    }

    /// <summary>Finds a node by its (unique) display name.</summary>
    public static NodeModel Node(GraphModel graph, string name)
    {
        var matches = graph.Nodes.Where(n => n.Name == name).ToList();
        Assert.True(matches.Count == 1, "Expected exactly one node named '" + name + "', found " + matches.Count + ".");
        return matches[0];
    }

    /// <summary>Reads a node's output value (first port unless a port name is given).</summary>
    public static object? Output(GraphModel graph, string nodeName, string? portName = null)
    {
        var node = Node(graph, nodeName);
        var port = portName == null
            ? node.OutPorts[0]
            : node.OutPorts.First(p => p.Name == portName);
        return port.Value;
    }

    /// <summary>Asserts the value is a list and returns its elements.</summary>
    public static List<object?> AsList(object? value)
    {
        var list = Assert.IsAssignableFrom<IList>(value);
        return list.Cast<object?>().ToList();
    }

    /// <summary>Asserts the value is a list of numbers and returns them as doubles.</summary>
    public static List<double> AsDoubles(object? value)
    {
        return AsList(value).Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToList();
    }
}
