using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// A resolved loop: its <see cref="LoopItemNode"/> boundary, its
/// <see cref="LoopCollectNode"/> boundary, and the body nodes (everything that
/// depends on the current item) in the order they run each iteration.
/// </summary>
internal sealed class LoopRegion
{
    public LoopRegion(LoopItemNode item, LoopCollectNode collect, List<NodeModel> body)
    {
        Item = item;
        Collect = collect;
        Body = body;
    }

    /// <summary>The opening boundary.</summary>
    public LoopItemNode Item { get; }

    /// <summary>The closing boundary.</summary>
    public LoopCollectNode Collect { get; }

    /// <summary>Body nodes in per-iteration execution order (excludes the boundaries).</summary>
    public List<NodeModel> Body { get; }

    /// <summary>Every node owned by the region: the boundaries plus the body.</summary>
    public IEnumerable<NodeModel> AllNodes()
    {
        yield return Item;
        foreach (var node in Body)
        {
            yield return node;
        }

        yield return Collect;
    }

}

/// <summary>The loops discovered in a graph, plus any structural problems found.</summary>
internal sealed class LoopPlan
{
    public List<LoopRegion> Regions { get; } = new List<LoopRegion>();

    /// <summary>Maps each region-owned node to its region (valid regions only).</summary>
    public Dictionary<NodeModel, LoopRegion> NodeToRegion { get; } = new Dictionary<NodeModel, LoopRegion>();

    /// <summary>Structural problems to surface as node warnings (node → message).</summary>
    public List<KeyValuePair<NodeModel, string>> Problems { get; } = new List<KeyValuePair<NodeModel, string>>();

    public bool HasLoops => Regions.Count > 0 || Problems.Count > 0;
}

/// <summary>
/// Discovers loop regions (<see cref="LoopItemNode"/> → body → <see cref="LoopCollectNode"/>)
/// so the engine can run each body once per item. A region's body is the set of
/// nodes that transitively depend on the item, bounded by the collect node.
/// Unpaired boundaries and nested/overlapping loops are reported as problems and
/// their regions are dropped (the boundaries then behave as inert nodes).
/// </summary>
internal static class LoopPlanner
{
    public static LoopPlan Plan(GraphModel graph)
    {
        var plan = new LoopPlan();
        var items = graph.Nodes.OfType<LoopItemNode>().ToList();
        var collects = graph.Nodes.OfType<LoopCollectNode>().ToList();
        if (items.Count == 0 && collects.Count == 0)
        {
            return plan;
        }

        foreach (var collect in collects)
        {
            var loopConnection = graph.FindConnectionInto(collect.InPorts[0]); // "loop" port
            if (loopConnection == null || !(loopConnection.SourceNode is LoopItemNode item))
            {
                plan.Problems.Add(new KeyValuePair<NodeModel, string>(
                    collect, "Loop.Collect's 'loop' input must be wired from a Loop.Item's 'loop' output."));
                continue;
            }

            if (plan.Regions.Any(r => r.Item == item))
            {
                plan.Problems.Add(new KeyValuePair<NodeModel, string>(
                    collect, "This Loop.Item is already closed by another Loop.Collect."));
                continue;
            }

            if (!TryComputeBody(graph, item, collect, out var body, out var problem))
            {
                plan.Problems.Add(new KeyValuePair<NodeModel, string>(collect, problem!));
                continue;
            }

            plan.Regions.Add(new LoopRegion(item, collect, body!));
        }

        foreach (var item in items)
        {
            if (!plan.Regions.Any(r => r.Item == item))
            {
                plan.Problems.Add(new KeyValuePair<NodeModel, string>(
                    item, "Loop.Item's 'loop' output must be wired into a Loop.Collect to run the loop."));
            }
        }

        // Build the membership map and reject overlap/nesting (a node in two regions).
        var overlapping = new List<LoopRegion>();
        foreach (var region in plan.Regions)
        {
            foreach (var node in region.AllNodes())
            {
                if (plan.NodeToRegion.TryGetValue(node, out var existing) && existing != region)
                {
                    overlapping.Add(region);
                    overlapping.Add(existing);
                    plan.Problems.Add(new KeyValuePair<NodeModel, string>(
                        region.Collect, "Loops overlap or nest, which is not supported yet — give each loop its own nodes."));
                    break;
                }

                plan.NodeToRegion[node] = region;
            }
        }

        if (overlapping.Count > 0)
        {
            foreach (var region in overlapping.Distinct())
            {
                plan.Regions.Remove(region);
                foreach (var node in region.AllNodes())
                {
                    if (plan.NodeToRegion.TryGetValue(node, out var owner) && owner == region)
                    {
                        plan.NodeToRegion.Remove(node);
                    }
                }
            }
        }

        return plan;
    }

    /// <summary>
    /// Body = every node transitively downstream of the item boundary, stopping at
    /// the collect boundary, returned in per-iteration topological order. Fails when
    /// another loop boundary is encountered inside the body (nesting).
    /// </summary>
    private static bool TryComputeBody(
        GraphModel graph,
        LoopItemNode item,
        LoopCollectNode collect,
        out List<NodeModel>? body,
        out string? problem)
    {
        body = null;
        problem = null;

        var members = new HashSet<NodeModel>();
        var queue = new Queue<NodeModel>();
        foreach (var seed in DownstreamTargets(graph, item))
        {
            if (seed == collect || seed == item)
            {
                continue;
            }

            if (members.Add(seed))
            {
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is LoopItemNode || node is LoopCollectNode)
            {
                problem = "Nested or overlapping loops are not supported yet — give each loop its own nodes.";
                return false;
            }

            foreach (var target in DownstreamTargets(graph, node))
            {
                if (target == collect || target == item)
                {
                    continue; // the collect node is the boundary; never re-enter the item
                }

                if (members.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        body = TopologicalOrder(graph, members);
        return true;
    }

    private static IEnumerable<NodeModel> DownstreamTargets(GraphModel graph, NodeModel node)
    {
        foreach (var port in node.OutPorts)
        {
            foreach (var connection in graph.FindConnectionsFrom(port))
            {
                yield return connection.TargetNode;
            }
        }
    }

    /// <summary>
    /// Kahn's algorithm over the body subset, counting only intra-body edges,
    /// tie-broken by canvas position (<see cref="ExecutionOrder"/>) so
    /// independent side-effect nodes run in the order they appear on screen.
    /// </summary>
    private static List<NodeModel> TopologicalOrder(GraphModel graph, HashSet<NodeModel> nodes)
    {
        var inDegree = nodes.ToDictionary(n => n, _ => 0);
        foreach (var connection in graph.Connections)
        {
            if (nodes.Contains(connection.SourceNode) && nodes.Contains(connection.TargetNode) &&
                connection.SourceNode != connection.TargetNode)
            {
                inDegree[connection.TargetNode]++;
            }
        }

        var ready = nodes.Where(n => inDegree[n] == 0).ToList();
        var order = new List<NodeModel>(nodes.Count);
        while (ready.Count > 0)
        {
            var next = ready[0];
            foreach (var candidate in ready)
            {
                if (ExecutionOrder.Compare(candidate, next) < 0)
                {
                    next = candidate;
                }
            }

            ready.Remove(next);
            order.Add(next);

            foreach (var connection in graph.Connections)
            {
                if (connection.SourceNode == next && nodes.Contains(connection.TargetNode) &&
                    connection.SourceNode != connection.TargetNode &&
                    --inDegree[connection.TargetNode] == 0)
                {
                    ready.Add(connection.TargetNode);
                }
            }
        }

        // Any leftover (cycle within the body — shouldn't happen for a valid DAG) is appended deterministically.
        if (order.Count != nodes.Count)
        {
            foreach (var node in nodes.OrderBy(n => n, Comparer<NodeModel>.Create(ExecutionOrder.Compare)))
            {
                if (!order.Contains(node))
                {
                    order.Add(node);
                }
            }
        }

        return order;
    }
}
