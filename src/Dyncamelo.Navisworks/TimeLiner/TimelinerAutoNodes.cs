// NOTE: Like TimelinerNodes.cs, this file compiles against the Timeliner
// assembly from the Chuongmep 2023.0.7 package (no 2024 Timeliner is published
// on NuGet); the host redirects the strong-named reference to its own loaded
// copy via AppDomain.AssemblyResolve — see the header of TimelinerNodes.cs.
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks.TimeLiner;

/// <summary>
/// Rule-based TimeLiner attachment — the UI's "Auto-Attach Using Rules",
/// scriptable. RUNTIME-CHECK (Windows smoke list): attaching explicit items via
/// TimelinerSelection.CopyFrom persists after TaskEdit; the verified fallback
/// is a search set per task + TimelinerTask.AttachSet.
/// </summary>
[NodeCategory("Navisworks.TimeLiner")]
public static class TimelinerAutoNodes
{
    /// <summary>Attaches items to every task whose name equals the items' property value.</summary>
    /// <param name="category">Property category display name (e.g. "Element" or a Properties.SetCustom tab). Internal names do not match.</param>
    /// <param name="property">Property display name (e.g. "Task Name"). Internal names do not match.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>How many tasks received attachments, and the names of tasks with no matching items.</returns>
    [NodeName("TimeLiner.AutoAttachByProperty")]
    [NodeDescription("For every TimeLiner task (subtasks included), finds all items whose property value equals the task name and attaches them — the UI's \"Auto-Attach Using Rules\", scriptable. Replaces each matched task's existing attachment; tasks with no matching items are left untouched and reported in unmatchedTasks.")]
    [NodeSearchTags("timeliner", "auto", "attach", "rules", "4d", "link", "schedule", "property")]
    [MultiReturn("attachedCount", "unmatchedTasks")]
    public static Dictionary<string, object?> AutoAttachByProperty(
        string category,
        string property,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("No property category name provided.", nameof(category));
        }

        if (string.IsNullOrEmpty(property))
        {
            throw new ArgumentException("No property name provided.", nameof(property));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        // Snapshot the tree as index paths first: TaskEdit replaces stored task
        // instances, so handles captured up front could go stale mid-run, while
        // index paths stay valid (TaskEdit never reorders).
        var taskPaths = new List<List<int>>();
        CollectTaskPaths(timeliner.Tasks, new List<int>(), taskPaths);

        int attached = 0;
        var unmatched = new List<string>();
        foreach (var path in taskPaths)
        {
            TimelinerTask stored;
            try
            {
                stored = timeliner.TaskResolveIndexPath(path);
            }
            catch (Exception)
            {
                continue; // tree changed underneath us; skip defensively
            }

            if (stored == null)
            {
                continue;
            }

            var taskName = stored.DisplayName;
            if (string.IsNullOrEmpty(taskName))
            {
                unmatched.Add(string.Empty);
                continue;
            }

            var items = SearchNodes.ByPropertyValue(category, property, taskName, doc);
            if (items.Count == 0)
            {
                unmatched.Add(taskName);
                continue;
            }

            var copy = stored.CreateCopy();
            copy.Selection.CopyFrom(NavisValues.ToItemCollection(items));
            CommitTaskEdit(timeliner, path, copy);
            attached++;
        }

        return new Dictionary<string, object?>
        {
            ["attachedCount"] = attached,
            ["unmatchedTasks"] = unmatched,
        };
    }

    /// <summary>
    /// Records the index path of every TimeLiner task, depth first: subtasks
    /// follow their parent, and non-task groups are descended through so the
    /// paths stay aligned with TaskResolveIndexPath's addressing.
    /// </summary>
    private static void CollectTaskPaths(
        IEnumerable<SavedItem> items, List<int> prefix, List<List<int>> paths)
    {
        int index = 0;
        foreach (var item in items)
        {
            var path = new List<int>(prefix) { index };
            if (item is TimelinerTask task)
            {
                paths.Add(path);
                CollectTaskPaths(task.Children, path, paths); // subtasks
            }
            else if (item is GroupItem group)
            {
                CollectTaskPaths(group.Children, path, paths);
            }

            index++;
        }
    }

    /// <summary>
    /// Commits an edited copy over the stored task at the given index path via
    /// the document part's TaskEdit (same pattern as TimelinerNodes).
    /// </summary>
    private static void CommitTaskEdit(DocumentTimeliner timeliner, List<int> path, TimelinerTask editedCopy)
    {
        if (path.Count == 1)
        {
            timeliner.TaskEdit(path[0], editedCopy);
            return;
        }

        var parentPath = new List<int>(path);
        parentPath.RemoveAt(parentPath.Count - 1);
        var parent = timeliner.TaskResolveIndexPath(parentPath);
        timeliner.TaskEdit(parent, path[path.Count - 1], editedCopy);
    }
}
