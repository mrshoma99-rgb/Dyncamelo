using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Tests.Fixtures;
using CamelGraph.Core.Types;
using Xunit;

namespace CamelGraph.Core.Tests;

/// <summary>A stand-in for a model element.</summary>
public sealed class PickedItem
{
    public PickedItem(string id) => Id = id;

    public string Id { get; }
}

/// <summary>Nodes with the three kinds of element port the Navisworks library has.</summary>
public static class PickFixtures
{
    public static int CountMulti([MultiInput] IEnumerable<PickedItem> items) => items.Count();

    public static int CountList(List<PickedItem> items) => items.Count;

    public static int CountIList(IList<PickedItem> items) => items.Count;

    public static string One(PickedItem item) => item.Id;
}

/// <summary>
/// A picked selection of N elements is pinned on an input as ONE text ("nw:path;path;...") that the host's converters resolve to the
/// elements. A list-typed input must receive all N, not the first (the engine used to wrap the text into a one-item list first).
/// </summary>
public class PickedSelectionTests
{
    private static readonly List<NodeDefinition> Definitions = AssemblyNodeLoader.LoadType(typeof(PickFixtures));

    static PickedSelectionTests()
    {
        // The same pair of converters the Navisworks pack registers: one text, either the first element or all of them.
        TypeCoercion.RegisterConverter(typeof(string), typeof(PickedItem), value => Resolve((string)value).FirstOrDefault());
        TypeCoercion.RegisterConverter(typeof(string), typeof(List<PickedItem>), value => Resolve((string)value));
    }

    private static List<PickedItem> Resolve(string text)
    {
        if (!text.StartsWith("pick:", StringComparison.Ordinal))
        {
            throw new InvalidCastException("'" + text + "' is not a picked selection.");
        }

        return text.Substring(5).Split(';').Where(s => s.Length > 0).Select(s => new PickedItem(s)).ToList();
    }

    private static ZeroTouchNodeModel Node(string method) =>
        new ZeroTouchNodeModel(Definitions.Single(d => d.Method.Name == method));

    private static ZeroTouchNodeModel RunPinned(string method, string pinned)
    {
        var graph = new GraphModel();
        var node = Node(method);
        graph.AddNode(node);
        node.InPorts[0].SetUserValue(pinned);
        new GraphEngine().Run(graph);
        return node;
    }

    [Theory]
    [InlineData("CountMulti")]
    [InlineData("CountList")]
    [InlineData("CountIList")]
    public void APickOfThreeElementsReachesAListInputAsThree(string method)
    {
        var node = RunPinned(method, "pick:a;b;c");

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(3, node.OutPorts[0].Value);
    }

    [Fact]
    public void APickOfThreeElementsStillGivesTheFirstToASingleElementInput()
    {
        var node = RunPinned("One", "pick:a;b;c");

        Assert.Equal("a", node.OutPorts[0].Value);
    }

    [Fact]
    public void APickWiredInAsAWireGivesAllElementsToo()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, "pick:x;y");
        var node = Node("CountMulti");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(2, node.OutPorts[0].Value);
    }

    [Fact]
    public void ATextThatIsNotAPickIsStillRefusedLikeBefore()
    {
        var node = RunPinned("CountMulti", "hello");

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Contains("Cannot convert", node.StateMessage);
        Assert.Null(node.OutPorts[0].Value);
    }

    [Fact]
    public void AnEmptyPickGivesAnEmptyList()
    {
        var node = RunPinned("CountList", "pick:");

        Assert.Equal(0, node.OutPorts[0].Value);
    }

    [Fact]
    public void AListOfItemsWiredInIsUntouched()
    {
        var graph = new GraphModel();
        var source = ZT.Value(graph, new List<object?> { new PickedItem("a"), new PickedItem("b") });
        var node = Node("CountMulti");
        graph.AddNode(node);
        ZT.Wire(graph, source, 0, node, 0);

        new GraphEngine().Run(graph);

        Assert.Equal(2, node.OutPorts[0].Value);
    }
}
