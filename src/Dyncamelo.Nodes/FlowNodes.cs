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
}
