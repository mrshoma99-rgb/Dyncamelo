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
    /// then declaration order. Inputs already wired are skipped when <paramref name="freeOnly"/>.
    /// </summary>
    public static PortModel? BestInputFor(GraphModel graph, PortModel output, NodeModel node, bool freeOnly, int worstAcceptable = 2)
    {
        PortModel? best = null;
        var bestScore = int.MaxValue;
        for (var i = 0; i < node.InPorts.Count; i++)
        {
            var input = node.InPorts[i];
            if (freeOnly && graph.FindConnectionInto(input) != null)
            {
                continue;
            }

            var rank = Rank(PortKinds.Compare(output, input));
            if (rank > worstAcceptable)
            {
                continue;
            }

            // Required inputs (no default) win ties, then earlier ports.
            var score = rank * 1000 + (input.HasDefault ? 100 : 0) + i;
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

        // Connecting into the target replaces the original wire (an input takes one wire).
        var second = graph.Connect(output!, target);
        if (!second.Success)
        {
            graph.Disconnect(first.Connection!);
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
        var plan = new List<(PortModel Source, PortModel Target)>();
        for (var j = 0; j < pairs.Length; j++)
        {
            if (pairs[j] < 0)
            {
                continue;
            }

            var feeding = graph.FindConnectionInto(node.InPorts[pairs[j]]);
            if (feeding == null)
            {
                continue;
            }

            foreach (var outgoing in graph.FindConnectionsFrom(node.OutPorts[j]))
            {
                plan.Add((feeding.Source, outgoing.Target));
            }
        }

        graph.RemoveNode(node);
        foreach (var (source, target) in plan)
        {
            if (graph.FindConnectionInto(target) == null && graph.Connect(source, target).Success)
            {
                bridged++;
            }
        }

        return bridged;
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
