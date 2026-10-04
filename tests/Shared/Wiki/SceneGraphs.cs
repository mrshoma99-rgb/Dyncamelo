using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;

namespace CamelGraph.TestSupport.Wiki;

/// <summary>Builds a graph from library nodes: put a node where you want it, set the values typed into its inputs, wire outputs to inputs.</summary>
internal sealed class Sketch
{
    private readonly NodeRegistry _registry;

    public Sketch(NodeRegistry registry, string name)
    {
        _registry = registry;
        Graph = new GraphModel { Name = name };
    }

    public GraphModel Graph { get; }

    /// <summary>Puts a library node (by its display name, <c>List.Range</c>) on the canvas.</summary>
    public ZeroTouchNodeModel Library(string libraryName, double x, double y, string? title = null)
    {
        var definition = _registry.Definitions.FirstOrDefault(d => d.Name == libraryName);
        if (definition == null)
        {
            throw new InvalidOperationException("The node '" + libraryName + "' is not in the library.");
        }

        var node = new ZeroTouchNodeModel(definition) { X = x, Y = y };
        if (title != null)
        {
            node.Name = title;
        }

        Graph.AddNode(node);
        return node;
    }

    /// <summary>Puts a node on the canvas.</summary>
    public T Place<T>(T node, double x, double y, string? title = null)
        where T : NodeModel
    {
        node.X = x;
        node.Y = y;
        if (title != null)
        {
            node.Name = title;
        }

        Graph.AddNode(node);
        return node;
    }

    /// <summary>Types a value into an input of a node.</summary>
    public void Set(NodeModel node, string input, object? value)
    {
        var port = node.FindInPort(input);
        if (port == null)
        {
            throw new InvalidOperationException("'" + node.Name + "' has no input '" + input + "'.");
        }

        port.SetUserValue(value);
    }

    /// <summary>Wires an output of one node to an input of another.</summary>
    public void Wire(NodeModel from, string output, NodeModel to, string input)
    {
        var source = from.FindOutPort(output);
        var target = to.FindInPort(input);
        if (source == null || target == null)
        {
            throw new InvalidOperationException("Cannot wire '" + from.Name + "." + output + "' to '" + to.Name + "." + input + "': a port is missing.");
        }

        var result = Graph.Connect(source, target);
        if (!result.Success)
        {
            throw new InvalidOperationException("Cannot wire '" + from.Name + "." + output + "' to '" + to.Name + "." + input + "': " + result.Message);
        }
    }
}

/// <summary>
/// A node that only shows one socket of a given kind and shape; it is how a picture of the socket kinds is made without a node per kind in
/// the library. The kind comes from the socket's declared type, by the type's name, as it does for the library's own nodes.
/// </summary>
internal sealed class KindNode : NodeModel
{
    public KindNode(string name, string portName, Type type)
    {
        Name = name;
        Category = "Sockets";
        AddOutput(portName, type);
    }

    public override string NodeType => "WikiKind";

    public override object?[] Evaluate(object?[] inputs, EvaluationContext context) => new object?[] { null };
}

// Types that only carry the name of the host's type (the editor colours a socket by the name of its type).
internal sealed class ModelItem
{
}

internal sealed class ModelItemCollection
{
}

internal sealed class Viewpoint
{
}

internal sealed class ClashTest
{
}

internal sealed class Document
{
}

internal sealed class IWorkflowAction
{
}

/// <summary>The graphs the code-made pictures of the wiki are drawn from, built from library nodes so they can be checked without a window.</summary>
internal static class SceneGraphs
{
    /// <summary>The sentence the failing node of <see cref="ErrorsAndWarnings"/> reports.</summary>
    public const string ErrorText = "Cannot convert '12 pcs' to a number. Expected an invariant-culture numeric string such as \"3.14\".";

    /// <summary>
    /// Text that is not a number turned into one (red), the node after it (amber: its input failed), and a branch behind a Flow.When
    /// whose condition is false (idle). After a run: one error, one warning.
    /// </summary>
    public static GraphModel ErrorsAndWarnings(NodeRegistry registry)
    {
        var s = new Sketch(registry, "Errors and warnings");
        var text = s.Place(new StringInputNode { Value = "12 pcs" }, 0, 0, "Quantity text");
        var parse = s.Library("String.ToNumber", 330, 0, "Text to number");
        var add = s.Library("Add", 660, 0, "Add one");
        s.Set(add, "b", 1d);
        s.Wire(text, "value", parse, "text");
        s.Wire(parse, "result", add, "a");

        var gate = s.Place(new BooleanToggleNode { Value = false }, 0, 200, "Export?");
        var range = s.Library("List.Range", 0, 330, "Numbers 1 to 5");
        s.Set(range, "start", 1d);
        s.Set(range, "end", 5d);
        var when = s.Library("Flow.When", 330, 240, "Only if Export is on");
        var count = s.Library("List.Count", 660, 240, "Count the numbers");
        var watch = s.Place(new WatchNode(), 960, 240, "Count");
        s.Wire(range, "list", when, "value");
        s.Wire(gate, "value", when, "condition");
        s.Wire(when, "value", count, "list");
        s.Wire(count, "count", watch, "value");
        return s.Graph;
    }

    /// <summary>
    /// A chain with a muted node in the middle (it passes its first input through) and a branch with a frozen node (it keeps what it
    /// last produced). Run once with the values below, then the frozen node is frozen.
    /// </summary>
    public static GraphModel MuteAndFreeze(NodeRegistry registry)
    {
        var s = new Sketch(registry, "Mute and freeze");
        var length = s.Place(new NumberInputNode { Value = 6 }, 0, 0, "Length");
        var times = s.Library("Multiply", 300, 0, "Times two (muted)");
        s.Set(times, "b", 2d);
        var plus = s.Library("Add", 600, 0, "Add allowance");
        s.Set(plus, "b", 1d);
        var shown = s.Place(new WatchNode(), 900, 0, "Total");
        s.Wire(length, "value", times, "a");
        s.Wire(times, "result", plus, "a");
        s.Wire(plus, "result", shown, "value");
        times.IsMuted = true;

        var area = s.Place(new NumberInputNode { Value = 9 }, 0, 230, "Area");
        var root = s.Library("Math.Sqrt", 300, 230, "Side (frozen)");
        var side = s.Place(new WatchNode(), 600, 230, "Side length");
        s.Wire(area, "value", root, "number");
        s.Wire(root, "result", side, "value");
        return s.Graph;
    }

    /// <summary>A number, two small calculations and a watch; the two calculations are turned into a node group named <paramref name="groupName"/>.</summary>
    public static GraphModel NodeGroup(NodeRegistry registry, string groupName, out GroupInstanceNode instance)
    {
        var s = new Sketch(registry, "Node group");
        var length = s.Place(new NumberInputNode { Value = 6 }, 0, 0, "Length");
        var times = s.Library("Multiply", 330, 0, "Times");
        s.Set(times, "b", 2.5d);
        var plus = s.Library("Add", 630, 0, "Add allowance");
        s.Set(plus, "b", 10d);
        var total = s.Place(new WatchNode(), 960, 0, "Total");
        s.Wire(length, "value", times, "a");
        s.Wire(times, "result", plus, "a");
        s.Wire(plus, "result", total, "value");

        var result = NodeGroupOps.MakeGroup(s.Graph, new NodeModel[] { times, plus }, groupName, null);
        if (!result.Success || result.Instance == null)
        {
            throw new InvalidOperationException("The node group could not be made: " + result.Message);
        }

        instance = result.Instance;
        return s.Graph;
    }

    /// <summary>A wide graph of many small calculations (11 rows of four nodes: 44 in all), enough to turn the minimap on.</summary>
    public static GraphModel ManyNodes(NodeRegistry registry, int rows = 11)
    {
        var s = new Sketch(registry, "Many nodes");
        for (var row = 0; row < rows; row++)
        {
            var y = row * 190d;
            var number = s.Place(new NumberInputNode { Value = 2 + row }, 0, y, "Value " + (row + 1));
            var times = s.Library("Multiply", 300, y, "Scale " + (row + 1));
            s.Set(times, "b", 1.5d);
            var plus = s.Library("Add", 600, y, "Offset " + (row + 1));
            s.Set(plus, "b", 10d);
            var watch = s.Place(new WatchNode(), 900, y, "Result " + (row + 1));
            s.Wire(number, "value", times, "a");
            s.Wire(times, "result", plus, "a");
            s.Wire(plus, "result", watch, "value");
        }

        return s.Graph;
    }

    /// <summary>One small node per socket kind, in the order the wiki lists them.</summary>
    public static GraphModel SocketKinds()
    {
        var kinds = new (string Name, string Port, Type Type)[]
        {
            ("Number", "value", typeof(double)),
            ("Integer", "value", typeof(int)),
            ("Boolean", "value", typeof(bool)),
            ("Text", "value", typeof(string)),
            ("DateTime", "value", typeof(DateTime)),
            ("Colour", "value", typeof(CamelGraph.Nodes.CamelGraphColor)),
            ("Geometry", "value", typeof(CamelGraph.Nodes.CamelGraphPoint)),
            ("Item", "value", typeof(ModelItem)),
            ("Selection", "value", typeof(ModelItemCollection)),
            ("Viewpoint", "value", typeof(Viewpoint)),
            ("Clash", "value", typeof(ClashTest)),
            ("Document", "value", typeof(Document)),
            ("Data", "value", typeof(Dictionary<string, object>)),
            ("File", "path", typeof(string)),
            ("Action", "value", typeof(IWorkflowAction)),
        };
        var graph = new GraphModel { Name = "Socket kinds" };
        for (var i = 0; i < kinds.Length; i++)
        {
            var node = new KindNode(kinds[i].Name, kinds[i].Port, kinds[i].Type) { X = (i % 5) * 190d, Y = (i / 5) * 90d };
            graph.AddNode(node);
        }

        return graph;
    }

    /// <summary>A single value, a list, a list of lists, and a socket that takes several wires (here three).</summary>
    public static GraphModel SocketShapes(NodeRegistry registry)
    {
        var s = new Sketch(registry, "Socket shapes");
        s.Place(new KindNode("Single value", "value", typeof(double)), 0, 0);
        s.Place(new KindNode("List", "value", typeof(List<double>)), 0, 110);
        s.Place(new KindNode("List of lists", "value", typeof(List<List<double>>)), 0, 220);

        var a = s.Place(new NumberInputNode { Value = 4 }, 330, 0, "First");
        var b = s.Place(new NumberInputNode { Value = 8 }, 330, 110, "Second");
        var c = s.Place(new NumberInputNode { Value = 15 }, 330, 220, "Third");
        var sum = s.Library("List.Sum", 640, 90, "Several wires");
        s.Wire(a, "value", sum, "list");
        s.Wire(b, "value", sum, "list");
        s.Wire(c, "value", sum, "list");
        return s.Graph;
    }

    /// <summary>A node that reads one property of a model item, as the magnifier example needs (its tab and property inputs have the search button).</summary>
    public static GraphModel PropertyValue(NodeRegistry registry)
    {
        var s = new Sketch(registry, "Property value");
        s.Library("Properties.Value", 0, 0);
        return s.Graph;
    }

    /// <summary>One node with an editor of every kind: a number, a text, a drop-down, a colour.</summary>
    public static GraphModel Anatomy(NodeRegistry registry)
    {
        var s = new Sketch(registry, "Node anatomy");
        var node = s.Library("FallHazard.FloorOpeningMap", 0, 0);
        s.Set(node, "level", 3.5d);
        s.Set(node, "units", "Meters");
        s.Set(node, "imagePath", "hazard-map.png");
        s.Set(node, "lowColor", "#FF2E7D32");
        s.Set(node, "highColor", "#FFC62828");
        return s.Graph;
    }
}
