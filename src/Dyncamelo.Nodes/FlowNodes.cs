using System.Collections.Generic;
using Dyncamelo.Core.Execution;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Execution-ordering helpers. A dataflow graph only guarantees that a node
/// runs after the nodes it takes data from — two write nodes with no wire
/// between them are independent, and the engine may run them in either order.
/// These nodes turn "run B after A" into a real data dependency.
/// </summary>
[NodeCategory("Workflow")]
public static class FlowNodes
{
    /// <summary>
    /// Passes <paramref name="value"/> through unchanged, but only after every
    /// wired <paramref name="after"/> input has produced its result — use it to
    /// pin the order of side-effect nodes. Example: wire Viewpoint.SetSectionBox's
    /// <c>done</c> into <c>after</c> and the viewpoint name into <c>value</c>,
    /// then feed the output to Viewpoint.SaveWithOverrides — the save now always
    /// happens after the section box is applied.
    /// </summary>
    /// <param name="value">The value to pass through unchanged.</param>
    /// <param name="after">Wire any output of the node that must run first (its value is ignored).</param>
    /// <param name="after2">Optional second node that must run first.</param>
    /// <param name="after3">Optional third node that must run first.</param>
    /// <returns>The <paramref name="value"/> input, unchanged.</returns>
    [NodeName("Flow.Then")]
    [return: NodeName("value")]
    [NodeDescription("Passes a value through unchanged AFTER the wired 'after' nodes have run — makes the execution order of side-effect nodes explicit (e.g. set a section box, THEN save the viewpoint).")]
    [NodeSearchTags("sequence", "order", "passthrough", "wait", "after", "chain", "depend")]
    public static object? Then(object? value, object? after, object? after2 = null, object? after3 = null)
    {
        return value;
    }

    /// <summary>
    /// Switches a branch on or off. When <paramref name="condition"/> is true the value passes through; when it is false the
    /// output carries nothing, and every node after it is skipped (shown idle, not red) instead of running. Use it to run
    /// side-effects only when something happened: export only if there are clashes, write the log only if the file exists.
    /// </summary>
    /// <param name="value">The value to pass on when the condition is true. Wire the output of the node that must have run.</param>
    /// <param name="condition">True lets the branch run; false skips everything wired after this node.</param>
    /// <returns>The <paramref name="value"/> input, or nothing when the condition is false.</returns>
    [NodeName("Flow.When")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [return: NodeName("value")]
    [NodeDescription("Runs the nodes wired after it only when the condition is true; when false they are skipped (idle, not an error) — e.g. export only if there are clashes.")]
    [NodeSearchTags("if", "only", "skip", "branch", "gate", "conditional", "run if", "guard")]
    public static object? When(object? value, bool condition)
    {
        return condition ? value : InactiveValue.Instance;
    }

    /// <summary>
    /// Lets a graph carry on after a node failed. When the node wired into <paramref name="value"/> ran fine its result passes
    /// through; when it failed, <paramref name="result"/> becomes the fallback and the reason arrives in <c>error</c> — nothing
    /// after this node turns red. Typical use: read a file that may not exist, fall back to an empty list, log the reason.
    /// </summary>
    /// <param name="value">The node output to try. Wire the node that might fail.</param>
    /// <param name="fallback">What to output when it failed (nothing by default).</param>
    /// <returns>The "result" (the value, or the fallback after a failure), whether it "failed", and the "error" text.</returns>
    [NodeName("Flow.Try")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Create)]
    [CatchesUpstreamErrors]
    [MultiReturn("result", "failed", "error")]
    [PortKinds("", "boolean", "text")]
    [NodeDescription("Carries on after a failure: gives the node's result, or your fallback plus the error text when that node failed — nothing after it turns red.")]
    [NodeSearchTags("try", "catch", "error", "recover", "fallback", "optional", "safe", "exception", "on error")]
    public static Dictionary<string, object> Try(object? value, object? fallback = null)
    {
        var error = value as UpstreamError;
        return new Dictionary<string, object>
        {
            ["result"] = error != null ? fallback! : value!,
            ["failed"] = error != null,
            ["error"] = error != null ? error.Message : string.Empty,
        };
    }
}
