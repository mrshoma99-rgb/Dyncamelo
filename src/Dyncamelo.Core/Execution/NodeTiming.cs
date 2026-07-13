using System;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// How long one node spent executing during a run, summed across every time it
/// ran (a loop-body node runs once per item, so <see cref="Executions"/> is the
/// item count and <see cref="Elapsed"/> is the total across all iterations).
/// Surfaced by <see cref="RunResult.NodeTimings"/> so a slow graph can be
/// profiled — which node actually eats the time, rather than guessing.
/// </summary>
public sealed class NodeTiming
{
    /// <summary>Creates a timing record.</summary>
    /// <param name="node">The node that was timed.</param>
    /// <param name="elapsed">Total wall-clock time spent evaluating the node this run.</param>
    /// <param name="executions">How many times the node evaluated (>1 inside a loop).</param>
    public NodeTiming(NodeModel node, TimeSpan elapsed, int executions)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        Name = node.Name;
        Elapsed = elapsed;
        Executions = executions;
    }

    /// <summary>The timed node.</summary>
    public NodeModel Node { get; }

    /// <summary>The node's display name at the time of the run.</summary>
    public string Name { get; }

    /// <summary>Total time spent evaluating the node this run (summed over loop iterations).</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Number of evaluations this run (the loop item count for a body node, else 1).</summary>
    public int Executions { get; }
}
