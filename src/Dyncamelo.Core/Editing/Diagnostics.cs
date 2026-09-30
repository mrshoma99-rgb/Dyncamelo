using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Editing;

/// <summary>How serious a problem is.</summary>
public enum ProblemSeverity
{
    /// <summary>The node failed.</summary>
    Error,

    /// <summary>The node ran with a diagnostic, or was skipped because something upstream failed.</summary>
    Warning,
}

/// <summary>One node that reported a problem in the last run.</summary>
public sealed class NodeProblem
{
    /// <summary>Creates a problem.</summary>
    public NodeProblem(NodeModel node, ProblemSeverity severity, string text)
    {
        Node = node;
        Severity = severity;
        Text = text;
    }

    /// <summary>The node that reported it.</summary>
    public NodeModel Node { get; }

    /// <summary>Error or warning.</summary>
    public ProblemSeverity Severity { get; }

    /// <summary>What the node said.</summary>
    public string Text { get; }
}

/// <summary>The problems of a graph, and moving between them.</summary>
public static class Problems
{
    /// <summary>
    /// Every node in the Error or Warning state. Errors come first (they are the causes; the warnings are often their
    /// consequences), each group reading left to right, then top to bottom.
    /// </summary>
    /// <param name="graph">The graph to look at.</param>
    public static IReadOnlyList<NodeProblem> Collect(GraphModel graph)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        var list = new List<NodeProblem>();
        foreach (var node in graph.Nodes)
        {
            if (node.State == NodeState.Error)
            {
                list.Add(new NodeProblem(node, ProblemSeverity.Error, Summarise(node, MessageSeverity.Error)));
            }
            else if (node.State == NodeState.Warning)
            {
                list.Add(new NodeProblem(node, ProblemSeverity.Warning, Summarise(node, MessageSeverity.Warning)));
            }
        }

        return list
            .OrderBy(p => p.Severity)
            .ThenBy(p => p.Node.X)
            .ThenBy(p => p.Node.Y)
            .ToList();
    }

    /// <summary>The problem after (or before) <paramref name="current"/>, wrapping round; the first one when the current node has none.</summary>
    /// <param name="problems">A list from <see cref="Collect"/>.</param>
    /// <param name="current">The node the user is on, or null.</param>
    /// <param name="forward">True for the next problem, false for the previous one.</param>
    /// <returns>The problem to go to, or null when there are none.</returns>
    public static NodeProblem? Step(IReadOnlyList<NodeProblem> problems, NodeModel? current, bool forward)
    {
        if (problems == null || problems.Count == 0)
        {
            return null;
        }

        var index = -1;
        for (var i = 0; i < problems.Count; i++)
        {
            if (ReferenceEquals(problems[i].Node, current))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return forward ? problems[0] : problems[problems.Count - 1];
        }

        var next = (index + (forward ? 1 : -1) + problems.Count) % problems.Count;
        return problems[next];
    }

    private static string Summarise(NodeModel node, MessageSeverity atLeast)
    {
        var texts = node.Messages.Where(m => m.Severity >= atLeast).Select(m => m.Text).Distinct().ToList();
        return texts.Count > 0 ? string.Join(" ", texts) : "This node reported a problem.";
    }
}

/// <summary>Says, in words, why a node has (or has not) produced results — the answer to "why didn't this run?".</summary>
public static class RunExplanation
{
    /// <summary>A short explanation of the node's state, naming the node responsible where another node is the reason.</summary>
    /// <param name="graph">The graph the node is in.</param>
    /// <param name="node">The node to explain.</param>
    public static string Explain(GraphModel graph, NodeModel node)
    {
        if (graph == null)
        {
            throw new ArgumentNullException(nameof(graph));
        }

        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        var name = "'" + node.Name + "'";
        if (node.IsFrozen)
        {
            return name + " is frozen: it is skipped, together with everything after it, and keeps its last results. Unfreeze it (Shift+M) to let it run.";
        }

        var frozenAncestor = GraphOps.Upstream(graph, new[] { node }).FirstOrDefault(n => n != node && n.IsFrozen);
        if (frozenAncestor != null)
        {
            return name + " is skipped because '" + frozenAncestor.Name + "' before it is frozen; it keeps its last results. Unfreeze '" + frozenAncestor.Name + "' (Shift+M) to let it run.";
        }

        if (node.IsMuted)
        {
            return name + " is muted: it does not run, and each output passes through the first input of a matching type. Unmute it (M) to run it.";
        }

        switch (node.State)
        {
            case NodeState.Error:
                return name + " failed: " + Text(node, MessageSeverity.Error);
            case NodeState.Warning:
                {
                    var failed = GraphOps.Upstream(graph, new[] { node })
                        .Where(n => n != node && n.State == NodeState.Error)
                        .Select(n => "'" + n.Name + "'")
                        .ToList();
                    return failed.Count > 0
                        ? name + " did not run because " + string.Join(", ", failed) + " before it failed. Fix that first."
                        : name + " ran with a warning: " + Text(node, MessageSeverity.Warning);
                }

            case NodeState.Idle:
                {
                    var waiting = node.Messages.Where(m => m.Severity == MessageSeverity.Info).Select(m => m.Text).ToList();
                    if (waiting.Count > 0)
                    {
                        return name + " is waiting for input: " + string.Join(" ", waiting);
                    }

                    return node.IsDirty
                        ? name + " has not run since it last changed. Press Run (F5), or turn on Auto-Run."
                        : name + " has not produced results.";
                }

            default:
                return node.IsDirty
                    ? name + " ran before, but something changed since; it runs again at the next Run."
                    : name + " ran successfully and its results are up to date.";
        }
    }

    private static string Text(NodeModel node, MessageSeverity atLeast)
    {
        var texts = node.Messages.Where(m => m.Severity >= atLeast).Select(m => m.Text).Distinct().ToList();
        return texts.Count > 0 ? string.Join(" ", texts) : "no details were reported.";
    }
}
