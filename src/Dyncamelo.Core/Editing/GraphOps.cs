using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>
/// The wiring edits behind the rope-style gestures: insert a node on a wire,
/// delete a node and keep the data flowing, connect a selection in a chain,
/// make room after an insertion, and follow links up/down the graph. All pure
/// graph operations (no UI), so they are unit-tested; callers wrap them in an
/// undo transaction.
/// </summary>
public static class GraphOps
{
    /// <summary>Minimum clear space, in pixels, kept between an inserted node and the next one.</summary>
    public const double InsertGap = 40d;

    // ----- choosing ports -------------------------------------------------------------

    /// <summary>Rank of a compatibility for picking a port: lower is better, 3 = unusable.</summary>
    public static int Rank(Compat compat)
    {
        switch (compat)
        {
            case Compat.Exact: return 0;
            case Compat.Convertible: return 1;
            case Compat.Loose: return 2;
            default: return 3;
        }
    }

    /// <summary>
    /// The best input of <paramref name="node"/> for a wire leaving <paramref name="output"/>:
    /// exact before convertible before loose; among equals, required inputs before optional,
    /// then declaration order. Inputs already wired are skipped when <paramref name="freeOnly"/> — except multi-input
    /// ports, which stay available (unless this very output is already wired into them) but rank behind a free single input.
    /// </summary>
    public static PortModel? BestInputFor(GraphModel graph, PortModel output, NodeModel node, bool freeOnly, int worstAcceptable = 2)
    {
        PortModel? best = null;
        var bestScore = int.MaxValue;
        for (var i = 0; i < node.InPorts.Count; i++)
        {
            var input = node.InPorts[i];
            var wired = graph.FindConnectionsInto(input);
            if (freeOnly && wired.Count > 0 && (!input.IsMultiInput || wired.Any(c => c.Source == output)))
            {
                continue;
            }

            var rank = Rank(PortKinds.Compare(output, input));
            if (rank > worstAcceptable)
            {
                continue;
            }

            // Required inputs (no default) win ties, then earlier ports; a multi-input that already has wires comes after a free one.
            var score = rank * 1000 + (input.HasDefault ? 100 : 0) + (input.IsMultiInput && wired.Count > 0 ? 50 : 0) + i;
            if (score < bestScore)
            {
                bestScore = score;
                best = input;
            }
        }

        return best;
    }

    /// <summary>The best output of <paramref name="node"/> to feed <paramref name="input"/>.</summary>
    public static PortModel? BestOutputFor(PortModel input, NodeModel node, int worstAcceptable = 2)
    {
        PortModel? best = null;
        var bestScore = int.MaxValue;
        for (var i = 0; i < node.OutPorts.Count; i++)
        {
            var output = node.OutPorts[i];
            var rank = Rank(PortKinds.Compare(output, input));
            if (rank > worstAcceptable)
            {
                continue;
            }

            var score = rank * 1000 + i;
            if (score < bestScore)
            {
                bestScore = score;
                best = output;
            }
        }

        return best;
    }

    // ----- insert on wire ----------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="node"/> (which must have no wires) can be inserted on
    /// <paramref name="wire"/>, and through which ports. Only a usable pair — an input the
    /// wire's source can feed and an output that can feed the wire's target — is offered.
    /// </summary>
    public static bool CanInsert(GraphModel graph, NodeModel node, ConnectionModel wire, out PortModel? input, out PortModel? output)
    {
        input = null;
        output = null;
        if (node == wire.SourceNode || node == wire.TargetNode || !graph.Connections.Contains(wire))
        {
            return false;
        }

        if (graph.Connections.Any(c => c.SourceNode == node || c.TargetNode == node))
        {
            return false;
        }

        input = BestInputFor(graph, wire.Source, node, freeOnly: true);
        output = BestOutputFor(wire.Target, node);
        return input != null && output != null;
    }

    /// <summary>Inserts the node on the wire: source → node → target. Returns false (changing nothing) when it cannot be done.</summary>
    public static bool InsertOnWire(GraphModel graph, NodeModel node, ConnectionModel wire)
    {
        if (!CanInsert(graph, node, wire, out var input, out var output))
        {
            return false;
        }

        var source = wire.Source;
        var target = wire.Target;
        var first = graph.Connect(source, input!);
        if (!first.Success)
        {
            return false;
        }

        // Connecting into a single-wire target replaces the original wire. A multi-input target keeps it, so the
        // original is removed and the new wire takes its place in the order.
        var multi = target.IsMultiInput;
        if (multi)
        {
            graph.Disconnect(wire);
        }

        var second = graph.Connect(output!, target, multi ? wire.Sequence : (int?)null);
        if (!second.Success)
        {
            graph.Disconnect(first.Connection!);
            if (multi)
            {
                graph.ReinsertConnection(wire);
            }

            return false;
        }

        return true;
    }

    // ----- delete and reconnect ------------------------------------------------------------

    /// <summary>
    /// Removes a node but keeps the data flowing: for every output paired with an input (the same
    /// pairing a muted node uses), whatever fed that input now feeds whatever the output fed.
    /// Wires that cannot be bridged are dropped. Returns the number of wires bridged.
    /// </summary>
    public static int DissolveNode(GraphModel graph, NodeModel node)
    {
        var bridged = 0;
        var pairs = MutePassThrough.Pair(node);
        var plan = new List<(PortModel Source, PortModel Target, int Order, PortModel First, int Offset)>();
        for (var j = 0; j < pairs.Length; j++)
        {
            if (pairs[j] < 0)
            {
                continue;
            }

            // A multi-input can be fed by several wires; each of them is bridged to each place the output went.
            var feeding = graph.FindConnectionsInto(node.InPorts[pairs[j]]);
            if (feeding.Count == 0)
            {
                continue;
            }

            foreach (var outgoing in graph.FindConnectionsFrom(node.OutPorts[j]))
            {
                for (var f = 0; f < feeding.Count; f++)
                {
                    // The first bridged wire takes the place of the outgoing one in a multi-input target's order,
                    // and the others follow it there.
                    plan.Add((feeding[f].Source, outgoing.Target, f == 0 ? outgoing.Sequence : -1, feeding[0].Source, f));
                }
            }
        }

        graph.RemoveNode(node);
        var made = new List<(PortModel Source, PortModel Target, PortModel First, int Offset)>();
        foreach (var (source, target, order, first, offset) in plan)
        {
            if ((target.IsMultiInput || graph.FindConnectionInto(target) == null) &&
                graph.Connect(source, target, target.IsMultiInput && order >= 0 ? order : (int?)null).Success)
            {
                bridged++;
                made.Add((source, target, first, offset));
            }
        }

        foreach (var (source, target, first, offset) in made)
        {
            if (offset == 0 || !target.IsMultiInput)
            {
                continue;
            }

            var wires = graph.FindConnectionsInto(target).ToList();
            var head = wires.FindIndex(w => w.Source == first);
            var wire = wires.Find(w => w.Source == source);
            if (head >= 0 && wire != null)
            {
                MoveWire(graph, wire, head + offset);
            }
        }

        return bridged;
    }

    // ----- swap links ----------------------------------------------------------------------

    /// <summary>
    /// Swaps the destinations of two wires: <c>A → X</c> and <c>B → Y</c> become <c>A → Y</c> and <c>B → X</c>.
    /// Changes nothing (and returns false) when either new pairing is not accepted.
    /// </summary>
    public static bool SwapLinks(GraphModel graph, ConnectionModel first, ConnectionModel second)
    {
        if (first == second || !graph.Connections.Contains(first) || !graph.Connections.Contains(second))
        {
            return false;
        }

        var sourceA = first.Source;
        var targetX = first.Target;
        var sourceB = second.Source;
        var targetY = second.Target;
        var orderA = first.Sequence;
        var orderB = second.Sequence;
        if (sourceA == sourceB)
        {
            return false;
        }

        // Two wires into the same multi-input socket: swapping them swaps their place in the order.
        if (targetX == targetY && !targetX.IsMultiInput)
        {
            return false;
        }

        graph.Disconnect(first);
        graph.Disconnect(second);
        var a = graph.Connect(sourceA, targetY, orderB);
        var b = a.Success ? graph.Connect(sourceB, targetX, orderA) : a;
        if (a.Success && b.Success)
        {
            return true;
        }

        // Put things back the way they were.
        if (a.Success && a.Connection != null)
        {
            graph.Disconnect(a.Connection);
        }

        graph.Connect(sourceA, targetX, orderA);
        graph.Connect(sourceB, targetY, orderB);
        return false;
    }

    // ----- wire order (multi-input) ------------------------------------------------------------

    /// <summary>
    /// Moves a wire to another place in the order of the wires feeding its multi-input socket (0 = first).
    /// The wires between the two places are re-made in their new order; a muted wire stays muted.
    /// Returns the re-made wire, or null (changing nothing) when the input is not multi-input or the wire is already there.
    /// </summary>
    public static ConnectionModel? MoveWire(GraphModel graph, ConnectionModel wire, int newIndex)
    {
        var port = wire.Target;
        var wires = graph.FindConnectionsInto(port);
        var from = wires.ToList().IndexOf(wire);
        if (!port.IsMultiInput || from < 0)
        {
            return null;
        }

        newIndex = Math.Max(0, Math.Min(wires.Count - 1, newIndex));
        if (newIndex == from)
        {
            return null;
        }

        var low = Math.Min(from, newIndex);
        var high = Math.Max(from, newIndex);
        var places = wires.Select(w => w.Sequence).ToList();        // ascending: the places in the order
        var order = wires.ToList();
        order.RemoveAt(from);
        order.Insert(newIndex, wire);

        foreach (var moved in wires.Skip(low).Take(high - low + 1))
        {
            graph.Disconnect(moved);
        }

        ConnectionModel? remade = null;
        for (var i = low; i <= high; i++)
        {
            var old = order[i];
            var made = graph.Connect(old.Source, port, places[i]);
            if (!made.Success)
            {
                continue;
            }

            if (old.IsMuted)
            {
                graph.SetConnectionMuted(made.Connection!, true);
            }

            if (old == wire)
            {
                remade = made.Connection;
            }
        }

        return remade;
    }

    // ----- auto-connect --------------------------------------------------------------------

    /// <summary>
    /// Chains the given nodes left to right: for each neighbouring pair, connects outputs of the left
    /// node to free inputs of the right node, best match first. The first link may be a loose match;
    /// further links must be exact or convertible. Returns the number of wires made.
    /// </summary>
    public static int AutoConnect(GraphModel graph, IReadOnlyList<NodeModel> nodes)
    {
        var ordered = nodes.OrderBy(n => n.X).ThenBy(n => n.Y).ToList();
        var made = 0;
        for (var i = 0; i + 1 < ordered.Count; i++)
        {
            var left = ordered[i];
            var right = ordered[i + 1];
            var linksThisPair = 0;
            foreach (var output in left.OutPorts)
            {
                var input = BestInputFor(graph, output, right, freeOnly: true, worstAcceptable: linksThisPair == 0 ? 2 : 1);
                if (input != null && graph.Connect(output, input).Success)
                {
                    made++;
                    linksThisPair++;
                }
            }
        }

        return made;
    }

    // ----- making room -----------------------------------------------------------------------

    /// <summary>
    /// After a node was inserted, how far to the right must the nodes downstream of it move so that a
    /// clear gap of <see cref="InsertGap"/> remains? Returns each node to move with its new X.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="inserted">The inserted node.</param>
    /// <param name="widthOf">Rendered width of a node.</param>
    public static IReadOnlyList<KeyValuePair<NodeModel, double>> MakeRoom(GraphModel graph, NodeModel inserted, Func<NodeModel, double> widthOf)
    {
        var right = inserted.X + widthOf(inserted) + InsertGap;
        var downstream = graph.CollectDownstream(inserted).Where(n => n != inserted).ToList();
        if (downstream.Count == 0)
        {
            return Array.Empty<KeyValuePair<NodeModel, double>>();
        }

        var nearest = downstream.Min(n => n.X);
        var shift = right - nearest;
        if (shift <= 0d || double.IsNaN(shift) || double.IsInfinity(shift))
        {
            return Array.Empty<KeyValuePair<NodeModel, double>>();
        }

        return downstream.Select(n => new KeyValuePair<NodeModel, double>(n, n.X + shift)).ToList();
    }

    // ----- frames ----------------------------------------------------------------------------------

    /// <summary>Clear space between a frame's edge and the items it wraps.</summary>
    public const double FramePadding = 20d;

    /// <summary>Height of a frame's title bar, above the items.</summary>
    public const double FrameHeader = 42d;

    /// <summary>The frame rectangle (x, y, width, height) that wraps the given item rectangles, or null when there are none.</summary>
    /// <param name="items">Item rectangles as (x, y, width, height).</param>
    public static (double X, double Y, double Width, double Height)? FrameAround(IEnumerable<(double X, double Y, double Width, double Height)> items)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        var any = false;
        foreach (var item in items)
        {
            any = true;
            left = Math.Min(left, item.X);
            top = Math.Min(top, item.Y);
            right = Math.Max(right, item.X + item.Width);
            bottom = Math.Max(bottom, item.Y + item.Height);
        }

        if (!any)
        {
            return null;
        }

        return (left - FramePadding, top - FramePadding - FrameHeader, right - left + FramePadding * 2d, bottom - top + FramePadding * 2d + FrameHeader);
    }

    // ----- following links -----------------------------------------------------------------------

    /// <summary>The nodes reachable downstream of the seeds (seeds included).</summary>
    public static IReadOnlyCollection<NodeModel> Downstream(GraphModel graph, IEnumerable<NodeModel> seeds)
    {
        var result = new HashSet<NodeModel>();
        foreach (var seed in seeds)
        {
            foreach (var node in graph.CollectDownstream(seed))
            {
                result.Add(node);
            }

            result.Add(seed);
        }

        return result;
    }

    /// <summary>The nodes that feed the seeds, directly or indirectly (seeds included).</summary>
    public static IReadOnlyCollection<NodeModel> Upstream(GraphModel graph, IEnumerable<NodeModel> seeds)
    {
        var result = new HashSet<NodeModel>();
        var stack = new Stack<NodeModel>(seeds);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!result.Add(node))
            {
                continue;
            }

            foreach (var wire in graph.Connections)
            {
                if (wire.TargetNode == node && !result.Contains(wire.SourceNode))
                {
                    stack.Push(wire.SourceNode);
                }
            }
        }

        return result;
    }

    /// <summary>Every node of the same kind (same library definition or node type) as any seed.</summary>
    public static IReadOnlyCollection<NodeModel> Similar(GraphModel graph, IEnumerable<NodeModel> seeds)
    {
        var keys = new HashSet<string>(seeds.Select(Key), StringComparer.Ordinal);
        return graph.Nodes.Where(n => keys.Contains(Key(n))).ToList();
    }

    private static string Key(NodeModel node) =>
        node is Dyncamelo.Core.Loader.ZeroTouchNodeModel zeroTouch ? zeroTouch.Definition.Id : node.NodeType;
}
