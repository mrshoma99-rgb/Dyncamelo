namespace Dyncamelo.Core.Workflow;

/// <summary>
/// A reified, deferred operation: an object that <em>describes</em> something to
/// do to the host state, and can be run later against a per-iteration
/// <see cref="WorkflowContext"/>.
/// <para>
/// This is the missing piece that lets the dataflow engine sequence stateful,
/// imperative effects <em>per item, in order</em>. Ordinary side-effect nodes
/// (isolate, zoom, save-viewpoint) act on one global mutable host state and,
/// under lacing, are evaluated stage-by-stage over the whole list (all zooms,
/// then all isolates, then all captures) — so a per-item "zoom → isolate →
/// capture" loop is impossible to express by wiring alone. Action-builder nodes
/// instead return an <see cref="IWorkflowAction"/> (a pure value, no effect yet),
/// and a single generic loop node (<c>Workflow.ForEach</c>) binds each item and
/// runs the action sequence against it, in order, one item at a time.
/// </para>
/// <para>
/// Implementations must be pure to construct (building one has no host effect)
/// and must perform their effect only inside <see cref="Run"/>.
/// </para>
/// </summary>
public interface IWorkflowAction
{
    /// <summary>
    /// A short human-readable label for the action, used in watch output and
    /// error messages (e.g. "Isolate", "Save viewpoint '{name}'"). No host effect.
    /// </summary>
    string Describe();

    /// <summary>
    /// Performs the action's effect against the current iteration. Called by the
    /// loop node once per item, after <paramref name="context"/> has been bound to
    /// that item. May read <see cref="WorkflowContext.CurrentItem"/> and collect a
    /// per-iteration result via <see cref="WorkflowContext.Collect"/>. Exceptions
    /// propagate to the loop node, which surfaces them on the graph.
    /// </summary>
    /// <param name="context">The current iteration's context.</param>
    void Run(WorkflowContext context);
}
