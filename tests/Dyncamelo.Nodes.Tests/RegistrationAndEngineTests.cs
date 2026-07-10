using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// End-to-end tests: the library registers cleanly, its zero-touch nodes run
/// under the engine (including replication and MultiReturn), the interactive
/// NodeModels behave, and graphs built from this library survive a .dyc
/// serialization round trip.
/// </summary>
public class RegistrationAndEngineTests
{
    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static ZeroTouchNodeModel CreateZeroTouch(NodeRegistry registry, string nodeName)
    {
        var definition = registry.Definitions.Single(d => d.Name == nodeName);
        return new ZeroTouchNodeModel(definition);
    }

    [Fact]
    public void RegisterAll_RegistersInteractiveNodeTypes()
    {
        var registry = CreateRegistry();
        Assert.IsType<ListCreateNode>(registry.CreateNode(ListCreateNode.TypeName));
        Assert.IsType<WatchListNode>(registry.CreateNode(WatchListNode.TypeName));
        Assert.IsType<ColorPickerNode>(registry.CreateNode(ColorPickerNode.TypeName));
    }

    [Fact]
    public void RegisterAll_ImportsEveryMvpZeroTouchNode()
    {
        var registry = CreateRegistry();
        var names = new HashSet<string>(registry.Definitions.Select(d => d.Name), StringComparer.Ordinal);

        var expected = new[]
        {
            // Math
            "Add", "Subtract", "Multiply", "Divide", "Modulo", "Math.Round", "Math.Min", "Math.Max",
            // Logic
            "If", "And", "Or", "Not", "Equals", "GreaterThan", "LessThan",
            // String
            "String.Concat", "String.Contains", "String.Split", "String.Replace",
            "String.Length", "String.ToNumber", "String.FromObject", "String.Join",
            // List (List.Create is a NodeModel, not zero-touch)
            "List.GetItemAtIndex", "List.Count", "List.FirstItem", "List.Flatten",
            "List.FilterByBoolMask", "List.Range", "List.Sort", "List.UniqueItems",
            // Dictionary
            "Dictionary.ByKeysValues", "Dictionary.ValueAtKey",
            // Color
            "Color.ByARGB",
            // DateTime
            "DateTime.Now", "DateTime.Format",
            // File
            "CSV.ReadFromFile", "CSV.WriteToFile", "Text.ReadFromFile", "Text.WriteToFile",
            "JSON.ReadFromFile", "JSON.WriteToFile",
            // Geometry
            "Point.ByCoordinates", "Point.Components", "BoundingBox.ByCorners", "BoundingBox.Center",
        };

        foreach (var name in expected)
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void RegisterAll_ImportsEveryBetaZeroTouchNode()
    {
        var registry = CreateRegistry();
        var names = new HashSet<string>(registry.Definitions.Select(d => d.Name), StringComparer.Ordinal);

        var expected = new[]
        {
            // Math
            "Math.Abs", "Math.Pow", "Math.Sqrt", "Math.Floor", "Math.Ceiling", "Math.MapRange", "Math.Random",
            // Logic
            "GreaterThanOrEqual", "LessThanOrEqual",
            // String
            "String.StartsWith", "String.EndsWith", "String.Substring",
            "String.ToUpper", "String.ToLower", "String.Trim",
            // List
            "List.LastItem", "List.Contains", "List.IndexOf", "List.Reverse",
            "List.AddItemToEnd", "List.Join", "List.RemoveItemAtIndex",
            "List.GroupByKey", "List.SortByKey",
            // Dictionary
            "Dictionary.Keys", "Dictionary.Values", "Dictionary.SetValueAtKey",
            // Color
            "Color.FromHex", "Color.Components", "Color.Lerp",
            // DateTime
            "DateTime.Parse", "DateTime.ByDate", "DateTime.AddDays", "DateTime.DaysBetween",
            // File
            "JSON.Parse", "JSON.Stringify", "File.Exists", "Directory.GetFiles", "Path.Combine",
            // Geometry
            "Point.DistanceTo", "Vector.ByCoordinates", "BoundingBox.Size", "BoundingBox.Intersects",
        };

        foreach (var name in expected)
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void Engine_MultiReturnGroupByKey_SplitsIntoPorts()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var items = new ListCreateNode();
        var keys = new ListCreateNode();
        var group = CreateZeroTouch(registry, "List.GroupByKey");

        var a = new StringInputNode { Value = "duct" };
        var b = new StringInputNode { Value = "pipe" };
        var c = new StringInputNode { Value = "tray" };
        var keyA = new StringInputNode { Value = "HVAC" };
        var keyB = new StringInputNode { Value = "Plumbing" };
        var keyC = new StringInputNode { Value = "HVAC" };

        foreach (var node in new NodeModel[] { items, keys, group, a, b, c, keyA, keyB, keyC })
        {
            graph.AddNode(node);
        }

        items.AddItemPort();
        items.AddItemPort();
        keys.AddItemPort();
        keys.AddItemPort();
        Assert.True(graph.Connect(a.OutPorts[0], items.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], items.InPorts[1]).Success);
        Assert.True(graph.Connect(c.OutPorts[0], items.InPorts[2]).Success);
        Assert.True(graph.Connect(keyA.OutPorts[0], keys.InPorts[0]).Success);
        Assert.True(graph.Connect(keyB.OutPorts[0], keys.InPorts[1]).Success);
        Assert.True(graph.Connect(keyC.OutPorts[0], keys.InPorts[2]).Success);
        Assert.True(graph.Connect(items.OutPorts[0], group.InPorts[0]).Success);
        Assert.True(graph.Connect(keys.OutPorts[0], group.InPorts[1]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, group.State);
        Assert.Equal(new[] { "groups", "uniqueKeys" }, group.OutPorts.Select(p => p.Name));
        var uniqueKeys = Assert.IsAssignableFrom<IEnumerable<object?>>(group.OutPorts[1].Value);
        Assert.Equal(new object?[] { "HVAC", "Plumbing" }, uniqueKeys.ToArray());
    }

    [Fact]
    public void RegisterAll_DoesNotImportHelpers()
    {
        var registry = CreateRegistry();
        Assert.DoesNotContain(registry.Definitions, d => d.Id.Contains("NodeLibrary"));
        Assert.DoesNotContain(registry.Definitions, d => d.Id.Contains("ValueComparison"));
    }

    [Fact]
    public void Engine_RunsZeroTouchAdd()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var a = new NumberInputNode { Value = 2 };
        var b = new NumberInputNode { Value = 3 };
        var add = CreateZeroTouch(registry, "Add");
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(add);
        Assert.True(graph.Connect(a.OutPorts[0], add.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], add.InPorts[1]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, add.State);
        Assert.Equal(5d, add.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_ReplicatesAddOverListCreateOutput()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var x = new NumberInputNode { Value = 2 };
        var y = new NumberInputNode { Value = 3 };
        var offset = new NumberInputNode { Value = 10 };
        var listCreate = new ListCreateNode();
        var add = CreateZeroTouch(registry, "Add");

        graph.AddNode(x);
        graph.AddNode(y);
        graph.AddNode(offset);
        graph.AddNode(listCreate);
        graph.AddNode(add);
        listCreate.AddItemPort();
        Assert.True(graph.Connect(x.OutPorts[0], listCreate.InPorts[0]).Success);
        Assert.True(graph.Connect(y.OutPorts[0], listCreate.InPorts[1]).Success);
        Assert.True(graph.Connect(listCreate.OutPorts[0], add.InPorts[0]).Success);
        Assert.True(graph.Connect(offset.OutPorts[0], add.InPorts[1]).Success);

        new GraphEngine().Run(graph);

        var values = Assert.IsAssignableFrom<IEnumerable<object?>>(add.OutPorts[0].Value);
        Assert.Equal(new object?[] { 12d, 13d }, values.ToArray());
    }

    [Fact]
    public void Engine_MultiReturnPointComponents_SplitsIntoPorts()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var seven = new NumberInputNode { Value = 7 };
        var point = CreateZeroTouch(registry, "Point.ByCoordinates");
        var components = CreateZeroTouch(registry, "Point.Components");

        graph.AddNode(seven);
        graph.AddNode(point);
        graph.AddNode(components);
        Assert.True(graph.Connect(seven.OutPorts[0], point.InPorts[0]).Success); // x = 7, y/z default 0
        Assert.True(graph.Connect(point.OutPorts[0], components.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, components.State);
        Assert.Equal(new[] { "x", "y", "z" }, components.OutPorts.Select(p => p.Name));
        Assert.Equal(7d, components.OutPorts[0].Value);
        Assert.Equal(0d, components.OutPorts[1].Value);
        Assert.Equal(0d, components.OutPorts[2].Value);
    }

    [Fact]
    public void Engine_NodeException_BecomesErrorState_NotACrash()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var text = new StringInputNode { Value = "not a number" };
        var toNumber = CreateZeroTouch(registry, "String.ToNumber");
        graph.AddNode(text);
        graph.AddNode(toNumber);
        Assert.True(graph.Connect(text.OutPorts[0], toNumber.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Error, toNumber.State);
        Assert.Contains("not a number", toNumber.StateMessage);
    }

    [Fact]
    public void ListCreateNode_GrowsAndShrinks_KeepingAtLeastOnePort()
    {
        var node = new ListCreateNode();
        Assert.Equal(1, node.ItemCount);
        Assert.Equal("item0", node.InPorts[0].Name);

        node.AddItemPort();
        node.AddItemPort();
        Assert.Equal(3, node.ItemCount);
        Assert.Equal("item2", node.InPorts[2].Name);

        Assert.True(node.RemoveItemPort());
        Assert.True(node.RemoveItemPort());
        Assert.False(node.RemoveItemPort()); // never below one port
        Assert.Equal(1, node.ItemCount);
    }

    [Fact]
    public void ListCreateNode_RemoveItemPort_DisconnectsItsWire()
    {
        var graph = new GraphModel();
        var number = new NumberInputNode { Value = 1 };
        var listCreate = new ListCreateNode();
        graph.AddNode(number);
        graph.AddNode(listCreate);
        listCreate.AddItemPort();
        Assert.True(graph.Connect(number.OutPorts[0], listCreate.InPorts[1]).Success);
        Assert.Single(graph.Connections);

        Assert.True(listCreate.RemoveItemPort());

        Assert.Empty(graph.Connections);
        Assert.Equal(1, listCreate.ItemCount);
    }

    [Fact]
    public void WatchListNode_FormatsListElementsPerLine_AndPassesValueThrough()
    {
        var node = new WatchListNode();
        var value = new List<object?> { 1.5, "a", null };

        var outputs = node.Evaluate(new object?[] { value }, new EvaluationContext());

        Assert.Same(value, outputs[0]);
        Assert.Equal(new[] { "0 : 1.5", "1 : a", "2 : null" }, node.Lines);
    }

    [Fact]
    public void WatchListNode_ScalarValue_DisplaysAsSingleLine()
    {
        var node = new WatchListNode();
        node.Evaluate(new object?[] { 42d }, new EvaluationContext());
        Assert.Equal(new[] { "42" }, node.Lines);
    }

    [Fact]
    public void Serialization_RoundTripsLibraryNodes_AndGraphStillRuns()
    {
        var registry = CreateRegistry();
        var serializer = new GraphSerializer(registry);

        var graph = new GraphModel { Name = "Nodes round trip" };
        var x = new NumberInputNode { Value = 2 };
        var y = new NumberInputNode { Value = 3 };
        var listCreate = new ListCreateNode();
        var count = CreateZeroTouch(registry, "List.Count");
        var picker = new ColorPickerNode { R = 10, G = 20, B = 30, A = 200 };

        graph.AddNode(x);
        graph.AddNode(y);
        graph.AddNode(listCreate);
        graph.AddNode(count);
        graph.AddNode(picker);
        listCreate.AddItemPort();
        listCreate.AddItemPort(); // three item ports, all wired below
        Assert.True(graph.Connect(x.OutPorts[0], listCreate.InPorts[0]).Success);
        Assert.True(graph.Connect(y.OutPorts[0], listCreate.InPorts[1]).Success);
        Assert.True(graph.Connect(x.OutPorts[0], listCreate.InPorts[2]).Success);
        Assert.True(graph.Connect(listCreate.OutPorts[0], count.InPorts[0]).Success);

        var json = serializer.Serialize(graph);
        var restored = serializer.Deserialize(json);

        var restoredListCreate = Assert.IsType<ListCreateNode>(
            restored.Nodes.Single(n => n.NodeType == ListCreateNode.TypeName));
        Assert.Equal(3, restoredListCreate.ItemCount);

        var restoredPicker = Assert.IsType<ColorPickerNode>(
            restored.Nodes.Single(n => n.NodeType == ColorPickerNode.TypeName));
        Assert.Equal(new DyncameloColor(200, 10, 20, 30), restoredPicker.Value);

        var restoredCount = Assert.IsType<ZeroTouchNodeModel>(
            restored.Nodes.Single(n => n.NodeType == ZeroTouchNodeModel.TypeName));
        Assert.Equal(4, restored.Connections.Count);

        new GraphEngine().Run(restored);
        Assert.Equal(NodeState.Executed, restoredCount.State);
        Assert.Equal(3, restoredCount.OutPorts[0].Value);
    }

    [Fact]
    public void DefinitionIds_AreStableManglings()
    {
        var registry = CreateRegistry();
        Assert.True(registry.TryGetDefinition("Dyncamelo.Nodes.MathNodes.Add@double,double", out var add));
        Assert.Equal("Add", add!.Name);
        Assert.Equal("Math", add.Category);
        Assert.Equal("Dyncamelo.Nodes", add.AssemblyName);
    }
}
