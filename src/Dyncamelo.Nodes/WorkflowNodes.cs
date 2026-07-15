using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Workflow;

namespace Dyncamelo.Nodes;

/// <summary>
/// The generic per-item loop that turns a list plus an ordered sequence of
/// reified <see cref="IWorkflowAction"/>s into a stateful, item-by-item workflow.
/// The loop is host-agnostic; the actions (isolate, zoom, save-viewpoint, ...)
/// are supplied by host node packs.
/// </summary>
[NodeCategory("Workflow")]
public static class WorkflowNodes
{
    /// <summary>
    /// Runs an ordered sequence of actions against each item in turn: item 0's
    /// actions all run (in order) before item 1's, and so on.
    /// </summary>
    /// <param name="items">The items to iterate. Each element becomes the current
    /// item for one pass; an element may itself be a group (a sub-list).</param>
    /// <param name="actions">The actions to run per item, in order — build this
    /// with List.Create from Action.* builder nodes.</param>
    /// <returns>One entry per item: the value that item's actions collected
    /// (unwrapped when a single value, a list when several, the item itself when
    /// none). For a per-item Save Viewpoint workflow this is the list of created
    /// viewpoints.</returns>
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [NodeName("Workflow.ForEach")]
    [NodeDescription(
        "Runs a sequence of actions on each item, one item fully before the next — " +
        "the per-item ordered loop (zoom → isolate → save viewpoint → next) that " +
        "wiring and lacing cannot express. Wire items and a List.Create of Action.* nodes.")]
    [NodeSearchTags("workflow", "foreach", "for each", "loop", "iterate", "sequence", "per item", "batch", "each")]
    [return: NodeName("results")]
    public static List<object?> ForEach(IEnumerable<object?> items, IEnumerable<object?> actions)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No items provided to iterate.");
        }

        if (actions == null)
        {
            throw new ArgumentNullException(nameof(actions), "No actions provided. Wire a List.Create of Action.* nodes.");
        }

        var itemList = Materialize(items);
        var actionList = new List<IWorkflowAction>();
        foreach (var candidate in actions)
        {
            // Non-action elements (a stray value wired into the sequence) are
            // skipped rather than aborting the whole run.
            if (candidate is IWorkflowAction action)
            {
                actionList.Add(action);
            }
        }

        var context = new WorkflowContext();
        var results = new List<object?>(itemList.Count);
        for (int index = 0; index < itemList.Count; index++)
        {
            context.Bind(itemList[index], index, itemList.Count);
            foreach (var action in actionList)
            {
                action.Run(context);
            }

            results.Add(CollectResult(context, itemList[index]));
        }

        return results;
    }

    private static object? CollectResult(WorkflowContext context, object? item)
    {
        var collected = context.Collected;
        if (collected.Count == 0)
        {
            // No action produced a result — pass the item through so the loop's
            // output is still a usable one-per-item list.
            return item;
        }

        if (collected.Count == 1)
        {
            return collected[0];
        }

        return new List<object?>(collected);
    }

    private static List<object?> Materialize(IEnumerable<object?> items)
    {
        if (items is List<object?> alreadyList)
        {
            return alreadyList;
        }

        var list = new List<object?>();
        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }
}
