using System;
using System.Collections.Generic;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Execution;

/// <summary>
/// Summary of one <see cref="GraphEngine.Run"/> invocation. Node-level failures
/// are reported on the nodes themselves; a run "succeeds" unless it was cancelled.
/// </summary>
public class RunResult
{
    private static readonly IReadOnlyList<NodeTiming> NoTimings = new List<NodeTiming>();

    /// <summary>Creates a run result.</summary>
    /// <param name="executedNodes">Nodes that actually executed, in execution order.</param>
    /// <param name="cancelled">True when the run stopped early due to cancellation.</param>
    /// <param name="elapsed">Wall-clock duration of the run.</param>
    /// <param name="nodeTimings">Per-node execution times (for profiling); empty when not collected.</param>
    public RunResult(
        IReadOnlyList<NodeModel> executedNodes,
        bool cancelled,
        TimeSpan elapsed,
        IReadOnlyList<NodeTiming>? nodeTimings = null)
    {
        ExecutedNodes = executedNodes ?? throw new ArgumentNullException(nameof(executedNodes));
        Cancelled = cancelled;
        Elapsed = elapsed;
        NodeTimings = nodeTimings ?? NoTimings;
    }

    /// <summary>Nodes that executed during this run, in execution order. Clean (cached) and frozen nodes are absent.</summary>
    public IReadOnlyList<NodeModel> ExecutedNodes { get; }

    /// <summary>True when the run was cancelled between nodes; remaining dirty nodes stay dirty.</summary>
    public bool Cancelled { get; }

    /// <summary>Wall-clock duration of the run.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// Per-node execution time, one entry per node that ran, summed across loop
    /// iterations. Empty unless profiling was collected. Not ordered — sort by
    /// <see cref="NodeTiming.Elapsed"/> to find the hotspots.
    /// </summary>
    public IReadOnlyList<NodeTiming> NodeTimings { get; }

    /// <summary>True when the run completed without cancellation.</summary>
    public bool Success => !Cancelled;
}
