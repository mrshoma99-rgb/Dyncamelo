namespace Dyncamelo.Core.Graph;

/// <summary>
/// Visual/diagnostic state of a node after (or before) execution.
/// </summary>
public enum NodeState
{
    /// <summary>The node has not executed yet, or could not execute because a required input is missing.</summary>
    Idle,

    /// <summary>The node executed successfully with no diagnostics.</summary>
    Executed,

    /// <summary>The node executed but produced diagnostics (bad data, null elements, coercion failures, upstream failure).</summary>
    Warning,

    /// <summary>The node is structurally broken or threw an exception during execution.</summary>
    Error,
}
