using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;

namespace CamelGraph.Core.Editing;

/// <summary>A node in a graph that can do something outside the model and the graph, and what.</summary>
public sealed class EffectFinding
{
    /// <summary>Creates a finding.</summary>
    /// <param name="nodeName">The node's name as shown on the canvas.</param>
    /// <param name="effect">One effect (a single flag of <see cref="NodeEffects"/>).</param>
    public EffectFinding(string nodeName, NodeEffects effect)
    {
        NodeName = nodeName;
        Effect = effect;
    }

    /// <summary>The node's name.</summary>
    public string NodeName { get; }

    /// <summary>The effect (one flag).</summary>
    public NodeEffects Effect { get; }
}

/// <summary>
/// Finds the nodes of a graph that start programs, use the network or change existing files. A graph that comes from a file somebody else
/// wrote is only run after the user has been told about them (see <c>GraphEditorViewModel</c>).
/// </summary>
public static class GraphEffects
{
    /// <summary>
    /// The nodes of the graph, and of the node groups it uses, that can do something outside the model and the graph. Muted and frozen
    /// nodes are left out (they do not run).
    /// </summary>
    /// <param name="graph">The document graph.</param>
    public static IReadOnlyList<EffectFinding> Survey(GraphModel graph)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var found = new List<EffectFinding>();
        Walk(graph, new HashSet<NodeGroup>(), found);
        return found
            .GroupBy(f => f.NodeName + "|" + (int)f.Effect, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Effect)
            .ThenBy(f => f.NodeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The short phrase for one effect, as used in the warning ("runs programs", "uses the network", "changes files").</summary>
    /// <param name="effect">A single flag.</param>
    public static string Phrase(NodeEffects effect)
    {
        switch (effect)
        {
            case NodeEffects.RunsPrograms:
                return "runs programs";
            case NodeEffects.UsesNetwork:
                return "uses the network";
            case NodeEffects.ChangesFiles:
                return "deletes, moves or overwrites files";
            default:
                return effect.ToString();
        }
    }

    /// <summary>The findings as lines for a message: "runs programs: System.Run, System.OpenPath" for each effect.</summary>
    /// <param name="findings">What <see cref="Survey"/> returned.</param>
    public static IReadOnlyList<string> Describe(IReadOnlyList<EffectFinding> findings)
    {
        if (findings == null)
        {
            throw new ArgumentNullException(nameof(findings));
        }

        return findings
            .GroupBy(f => f.Effect)
            .OrderBy(g => g.Key)
            .Select(g => Phrase(g.Key) + ": " + string.Join(", ", g.Select(f => f.NodeName).Distinct(StringComparer.OrdinalIgnoreCase).Take(6)) +
                         (g.Select(f => f.NodeName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 6 ? ", …" : string.Empty))
            .ToList();
    }

    private static void Walk(GraphModel graph, HashSet<NodeGroup> visited, List<EffectFinding> found)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.IsMuted || node.IsFrozen)
            {
                continue;
            }

            if (node is GroupInstanceNode instance)
            {
                if (instance.Definition != null && visited.Add(instance.Definition))
                {
                    Walk(instance.Definition.Graph, visited, found);
                }

                continue;
            }

            var effects = node.Effects;
            if (effects == NodeEffects.None)
            {
                continue;
            }

            foreach (NodeEffects flag in Enum.GetValues(typeof(NodeEffects)))
            {
                if (flag != NodeEffects.None && (effects & flag) == flag)
                {
                    found.Add(new EffectFinding(node.Name, flag));
                }
            }
        }
    }
}
