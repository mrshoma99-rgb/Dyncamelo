using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Workflow;

namespace CamelGraph.Nodes;

/// <summary>
/// The generic per-item loop that turns a list plus an ordered sequence of
/// reified <see cref="IWorkflowAction"/>s into a stateful, item-by-item workflow.
/// The loop is host-agnostic; the actions (isolate, zoom, save-viewpoint, ...)
/// are supplied by host node packs.
/// </summary>
[NodeCategory("Workflow")]
public static class WorkflowNodes
{
    /// <summary>The longest item name quoted in a message.</summary>
    private const int MaxNameInMessage = 60;

    /// <summary>How many failed items a warning lists by number.</summary>
    private const int MaxFailedListed = 10;

    /// <summary>
    /// Runs an ordered sequence of actions against each item in turn: item 0's
    /// actions all run (in order) before item 1's, and so on. The run can be
    /// stopped between two actions. By default a failing item is recorded as an
    /// empty result and the next item carries on; the node then shows one warning
    /// that names the items and the actions that failed.
    /// </summary>
    /// <param name="items">The items to iterate. Each element becomes the current
    /// item for one pass; an element may itself be a group (a sub-list).</param>
    /// <param name="actions">The actions to run per item, in order. Wire the Action.*
    /// nodes straight in (several wires run in the order they were made), or one
    /// List.Create of them. An element that is not an action is skipped with a warning.</param>
    /// <param name="onError">What a failing item does: "continue" (the default) records an
    /// empty result for it and goes on with the next item; "stop" ends the node with an error
    /// at the first failure (the items before it stay done).</param>
    /// <returns>One entry per item: the value that item's actions collected
    /// (unwrapped when a single value, a list when several, the item itself when
    /// none, empty when the item failed). For a per-item Save Viewpoint workflow this
    /// is the list of created viewpoints.</returns>
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeName("Workflow.ForEach")]
    [NodeAliases("CamelGraph.Nodes.WorkflowNodes.ForEach@System.Collections.Generic.IEnumerable<object>,System.Collections.Generic.IEnumerable<object>")]
    [NodeDescription(
        "Runs a sequence of actions on each item, one item fully before the next (zoom, isolate, save viewpoint, then the next item), " +
        "the per-item ordered loop that wiring and lacing cannot express. Wire the items and the Action.* nodes into 'actions' " +
        "(several wires run in the order they were made; a List.Create of them works too). " +
        "'results' has one entry per item: what its actions collected (one value as it is, several as a list, the item itself if the actions collect nothing), " +
        "or empty when that item failed. By default a failing item is skipped and the node ends amber with one line that names the items and actions that failed; " +
        "set onError to stop to end with an error at the first failure instead (what was already done stays done). " +
        "Press Stop to end it between two actions. " +
        "Use this node for a fixed, readable list of actions with {name} and {index1} naming (one viewpoint per item); " +
        "use Loop.Item and Loop.Collect when the work per item is built from ordinary nodes.")]
    [NodeSearchTags("workflow", "foreach", "for each", "loop", "iterate", "sequence", "per item", "batch", "each")]
    [return: NodeName("results")]
    public static List<object?> ForEach(
        IEnumerable<object?> items,
        [MultiInput] IEnumerable<object?> actions,
        [NodePanel("Advanced")][NodeChoices("continue", "stop")] string onError = "continue")
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No items provided to iterate.");
        }

        if (actions == null)
        {
            throw new ArgumentNullException(nameof(actions), "No actions provided. Wire the Action.* nodes (or a List.Create of them) into 'actions'.");
        }

        var stopAtFirstFailure = ReadOnError(onError);
        var itemList = Materialize(items);
        var actionList = ReadActions(actions);

        // The run this call belongs to (null in a unit test): between two actions it lets the host repaint and the user press Stop.
        var run = EvaluationContext.Current;
        var context = new WorkflowContext(run?.CancellationToken ?? default);
        var results = new List<object?>(itemList.Count);
        var failedItems = new List<int>();
        string? firstFailure = null;

        for (int index = 0; index < itemList.Count; index++)
        {
            run?.Checkpoint();
            context.Bind(itemList[index], index, itemList.Count);
            IWorkflowAction? running = null;
            try
            {
                foreach (var action in actionList)
                {
                    run?.Checkpoint();
                    running = action;
                    action.Run(context);
                }

                results.Add(CollectResult(context, itemList[index]));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) &&
                                       !(ex is OutOfMemoryException) &&
                                       !(ex is StackOverflowException))
            {
                var failure = DescribeFailure(context, running, ex);
                if (stopAtFirstFailure)
                {
                    throw new InvalidOperationException(
                        "Workflow.ForEach stopped: " + failure + " " +
                        index.ToString(CultureInfo.InvariantCulture) + " earlier item(s) are done and stay done. " +
                        "Set onError to continue to skip a failing item and go on with the next.",
                        ex);
                }

                firstFailure ??= failure;
                failedItems.Add(index + 1);
                results.Add(null);
            }
        }

        if (failedItems.Count > 0)
        {
            NodeWarnings.Add(SummariseFailures(failedItems, itemList.Count, firstFailure!));
        }

        return results;
    }

    private static bool ReadOnError(string? onError)
    {
        var text = (onError ?? string.Empty).Trim();
        if (text.Length == 0 || text.Equals("continue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new ArgumentException(
            "Workflow.ForEach: onError must be 'continue' or 'stop', not '" + text + "'.",
            nameof(onError));
    }

    /// <summary>The actions of the input, in order. Anything else in the list is skipped and reported in one warning.</summary>
    private static List<IWorkflowAction> ReadActions(IEnumerable<object?> actions)
    {
        var actionList = new List<IWorkflowAction>();
        var skipped = new List<string>();
        int position = 0;
        foreach (var candidate in actions)
        {
            position++;
            if (candidate is IWorkflowAction action)
            {
                actionList.Add(action);
            }
            else
            {
                skipped.Add("element " + position.ToString(CultureInfo.InvariantCulture) + " is " + DescribeElement(candidate));
            }
        }

        if (skipped.Count > 0)
        {
            var shown = skipped.Count > 3 ? string.Join("; ", skipped.GetRange(0, 3)) + "; ..." : string.Join("; ", skipped);
            NodeWarnings.Add(
                "Workflow.ForEach skipped " + skipped.Count.ToString(CultureInfo.InvariantCulture) +
                " element(s) of 'actions' that are not actions (" + shown + "). Wire Action.* nodes into 'actions'; an empty element is usually an Action node that failed." +
                (actionList.Count == 0 ? " No action is left, so the items come back unchanged." : string.Empty));
        }

        return actionList;
    }

    private static string DescribeElement(object? element)
    {
        switch (element)
        {
            case null:
                return "empty";
            case string text:
                return "the text \"" + Shorten(text) + "\"";
            default:
                return "a " + element.GetType().Name;
        }
    }

    private static string DescribeFailure(WorkflowContext context, IWorkflowAction? action, Exception error)
    {
        var item = "item " + context.Index1.ToString(CultureInfo.InvariantCulture);
        var name = SafeItemName(context);
        if (name.Length > 0)
        {
            item += " ('" + name + "')";
        }

        var step = action == null ? "an action" : "action '" + SafeDescribe(action) + "'";
        var reason = error.Message.Trim().TrimEnd('.');
        return item + ": " + step + " failed: " + (reason.Length == 0 ? error.GetType().Name : reason) + ".";
    }

    private static string SummariseFailures(List<int> failedItems, int total, string firstFailure)
    {
        var listed = failedItems.Count > MaxFailedListed
            ? string.Join(", ", failedItems.GetRange(0, MaxFailedListed).ConvertAll(i => i.ToString(CultureInfo.InvariantCulture))) + ", ..."
            : string.Join(", ", failedItems.ConvertAll(i => i.ToString(CultureInfo.InvariantCulture)));
        return "Workflow.ForEach: " + failedItems.Count.ToString(CultureInfo.InvariantCulture) + " of " +
               total.ToString(CultureInfo.InvariantCulture) + " item(s) failed and have an empty result (item " + listed +
               "); the actions that ran before the failing one stay applied. First failure: " + firstFailure;
    }

    private static string SafeItemName(WorkflowContext context)
    {
        try
        {
            return Shorten(context.ItemName);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SafeDescribe(IWorkflowAction action)
    {
        try
        {
            var text = action.Describe();
            return string.IsNullOrWhiteSpace(text) ? action.GetType().Name : text;
        }
        catch (Exception)
        {
            return action.GetType().Name;
        }
    }

    private static string Shorten(string text)
    {
        var flat = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            flat.Append(char.IsControl(ch) ? ' ' : ch);
        }

        var result = flat.ToString().Trim();
        return result.Length <= MaxNameInMessage ? result : result.Substring(0, MaxNameInMessage - 3) + "...";
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
