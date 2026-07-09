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
    /// <summary>Creates a run result.</summary>
    /// <param name="executedNodes">Nodes that actually executed, in execution order.</param>
    /// <param name="cancelled">True when the run stopped early due to cancellation.</param>
    /// <param name="elapsed">Wall-clock duration of the run.</param>
    public RunResult(IReadOnlyList<NodeModel> executedNodes, bool cancelled, TimeSpan elapsed)
    {
        ExecutedNodes = executedNodes ?? throw new ArgumentNullException(nameof(executedNodes));
        Cancelled = cancelled;
        Elapsed = elapsed;
    }

    /// <summary>Nodes that executed during this run, in execution order. Clean (cached) and frozen nodes are absent.</summary>
    public IReadOnlyList<NodeModel> ExecutedNodes { get; }

    /// <summary>True when the run was cancelled between nodes; remaining dirty nodes stay dirty.</summary>
    public bool Cancelled { get; }

    /// <summary>Wall-clock duration of the run.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>True when the run completed without cancellation.</summary>
    public bool Success => !Cancelled;
}
