using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Groups;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Serialization;

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
/// Finds the nodes of a graph that start programs, use the network, write or change files, or change the model. A graph that comes from
/// a file somebody else wrote is only run after the user has been told about them (see <c>GraphEditorViewModel</c>).
/// </summary>
public static class GraphEffects
{
    /// <summary>
    /// The nodes of the graph, and of the node groups it uses, that declare an effect (<see cref="NodeModel.Effects"/>): they start
    /// programs, use the network, write or change files, or change the model. Muted and frozen nodes are left out (they do not run).
    /// </summary>
    /// <param name="graph">The document graph.</param>
    public static IReadOnlyList<EffectFinding> Survey(GraphModel graph) => Survey(graph, false);

    /// <summary>
    /// Like <see cref="Survey(GraphModel)"/>, and optionally also counting a node whose role is Modify (<see cref="NodeModel.Function"/>)
    /// and that declares no effect of its own as one that <see cref="NodeEffects.ChangesModel"/>. The Script Player asks that wider
    /// question; the editor's question about graphs from a file asks only about declared effects. The hand-written nodes that ship
    /// with CamelGraph (List.Create, the loops, the displays...) are not counted for their default role, only for a declared effect.
    /// </summary>
    /// <param name="graph">The document graph.</param>
    /// <param name="modifyRoleChangesModel">True to report a Modify node with no declared effect as changing the model.</param>
    public static IReadOnlyList<EffectFinding> Survey(GraphModel graph, bool modifyRoleChangesModel)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var found = new List<EffectFinding>();
        Walk(graph, new HashSet<NodeGroup>(), found, modifyRoleChangesModel);
        return found
            .GroupBy(f => f.NodeName + "|" + (int)f.Effect, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => f.Effect)
            .ThenBy(f => f.NodeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The short phrase for one effect, as used in the warning ("runs programs", "uses the network", "writes files",
    /// "deletes, moves or overwrites files", "changes the model").
    /// </summary>
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
            case NodeEffects.WritesFiles:
                return "writes files";
            case NodeEffects.ChangesModel:
                return "changes the model";
            default:
                return effect.ToString();
        }
    }

    /// <summary>The findings as lines for a message: "runs programs: System.Run, System.OpenPath" for each effect.</summary>
    /// <param name="findings">What <see cref="Survey(GraphModel)"/> returned.</param>
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

    /// <summary>
    /// The findings as one sentence for a status line or a short message, with a capital first letter:
    /// "Changes the model: Isolate Walls; writes files: Text.WriteToFile". Empty when there are no findings.
    /// </summary>
    /// <param name="findings">What <see cref="Survey(GraphModel)"/> returned.</param>
    public static string Summarize(IReadOnlyList<EffectFinding> findings)
    {
        var lines = Describe(findings);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var text = string.Join("; ", lines);
        return char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    // The hand-written nodes that ship with CamelGraph (List.Create, Loop.Item, Watch List, Color Picker, Captured Selection...) never
    // chose a role, so they carry the catch-all default (Modify). That says nothing about the model: one that does change it
    // declares NodeEffects.ChangesModel. Library nodes (zero-touch) and nodes from other packs keep the role-based rule.
    private static bool IsBuiltInHandWritten(NodeModel node)
    {
        if (node is ZeroTouchNodeModel)
        {
            return false;
        }

        var assembly = node.GetType().Assembly.GetName().Name;
        return assembly == "CamelGraph.Core" || assembly == "CamelGraph.Nodes" || assembly == "CamelGraph.Navisworks";
    }

    private static void Walk(GraphModel graph, HashSet<NodeGroup> visited, List<EffectFinding> found, bool modifyRoleChangesModel)
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
                    Walk(instance.Definition.Graph, visited, found, modifyRoleChangesModel);
                }

                continue;
            }

            var effects = node.Effects;
            if (effects == NodeEffects.None && modifyRoleChangesModel && node.Function == NodeFunction.Modify && !(node is MissingNodeModel) &&
                !IsBuiltInHandWritten(node))
            {
                effects = NodeEffects.ChangesModel;
            }

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
