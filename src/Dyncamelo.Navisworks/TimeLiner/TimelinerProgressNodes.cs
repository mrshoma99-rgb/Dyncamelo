// NOTE: Like TimelinerNodes.cs, this file compiles against the Timeliner assembly from the
// Chuongmep 2023.0.7 package (no 2024 Timeliner is published on NuGet); the host redirects the
// strong-named reference to its own loaded copy via AppDomain.AssemblyResolve — see the header
// of TimelinerNodes.cs. The access pattern is the one TimelinerTask.SetDates uses: edit a copy of
// the stored task, commit it with DocumentTimeliner.TaskEdit at the task's index path, and
// re-resolve the stored instance afterwards (the document's TimeLiner part is fetched fresh on
// every call).
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;
using Dyncamelo.Nodes.Coordination;

namespace Dyncamelo.Navisworks.TimeLiner;

/// <summary>
/// TimeLiner schedule maintenance: progress, actual dates and deleting a task — the 4D
/// "update the programme from site" chores that TimelinerTask.SetDates did not cover.
/// </summary>
[NodeCategory("Navisworks.TimeLiner")]
public static class TimelinerProgressNodes
{
    /// <summary>Sets a TimeLiner task's progress.</summary>
    /// <param name="task">The stored TimeLiner task (from TimeLiner.Tasks or TimelinerTask.Create).</param>
    /// <param name="percent">Percent complete; values outside 0-100 are clamped to 0 or 100.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored task. Lace over task / percent lists for bulk progress updates.</returns>
    [NodeName("TimelinerTask.SetProgress")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [NodeDescription("Sets a task's percent complete (0-100; anything outside is clamped) — update progress from a site report in bulk. Lace over tasks and percentages.")]
    [NodeSearchTags("timeliner", "task", "progress", "percent", "complete", "status", "4d", "update")]
    [return: NodeName("task")]
    public static TimelinerTask SetProgress(
        TimelinerTask task,
        [NodeRange(0, 100, Step = 5, Unit = "%")] double percent,
        Document? document = null)
    {
        var timelinerTask = RequireTask(task, "TimelinerTask.SetProgress");
        var clamped = ScheduleRules.ClampPercent(percent);

        var doc = NavisworksContext.ResolveDocument(document);
        var copy = timelinerTask.CreateCopy();
        copy.ProgressPercent = clamped;
        return CommitTaskEdit(doc, timelinerTask, copy);
    }

    /// <summary>Sets a TimeLiner task's actual start and end dates.</summary>
    /// <param name="task">The stored TimeLiner task.</param>
    /// <param name="start">Actual start date.</param>
    /// <param name="end">Actual end date.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored task. Lace over lists for bulk updates.</returns>
    [NodeName("TimelinerTask.SetActual")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [NodeDescription("Sets a task's ACTUAL start and end dates (the planned dates are untouched; use TimelinerTask.SetDates for those) — record what happened on site next to the plan.")]
    [NodeSearchTags("timeliner", "task", "actual", "dates", "start", "end", "schedule", "4d", "update")]
    [return: NodeName("task")]
    public static TimelinerTask SetActual(
        TimelinerTask task,
        DateTime start,
        DateTime end,
        Document? document = null)
    {
        var timelinerTask = RequireTask(task, "TimelinerTask.SetActual");
        ScheduleRules.RequireDateRange(start, end, "actual", nameof(end));

        var doc = NavisworksContext.ResolveDocument(document);
        var copy = timelinerTask.CreateCopy();
        copy.ActualStartDate = start;
        copy.ActualEndDate = end;
        return CommitTaskEdit(doc, timelinerTask, copy);
    }

    /// <summary>Deletes a TimeLiner task.</summary>
    /// <param name="task">The stored TimeLiner task to delete.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the task was removed; false when it is not in the document.</returns>
    [NodeName("TimelinerTask.Delete")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Modify)]
    [NodeDescription("Deletes a TimeLiner task together with its subtasks. Returns false (and changes nothing) when the task is not in the document, so a clean-up step can run twice.")]
    [NodeSearchTags("timeliner", "task", "delete", "remove", "clean", "4d", "schedule")]
    [return: NodeName("deleted")]
    public static bool Delete(TimelinerTask task, Document? document = null)
    {
        var timelinerTask = RequireTask(task, "TimelinerTask.Delete");
        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var path = TryGetIndexPath(timeliner, timelinerTask);
        if (path == null || path.Count == 0)
        {
            return false;
        }

        if (path.Count == 1)
        {
            timeliner.TaskRemoveAt(path[0]);
        }
        else
        {
            var parentPath = new List<int>(path);
            parentPath.RemoveAt(parentPath.Count - 1);
            var parent = timeliner.TaskResolveIndexPath(parentPath);
            timeliner.TaskRemoveAt(parent, path[path.Count - 1]);
        }

        return true;
    }

    // ------------------------------------------------------------ privates

    private static TimelinerTask RequireTask(TimelinerTask? task, string nodeName)
    {
        return task ?? throw new ArgumentNullException(
            nameof(task),
            nodeName + " requires a TimeLiner task. Wire one from TimeLiner.Tasks or TimelinerTask.Create into the 'task' input.");
    }

    /// <summary>The index path of a stored task, or null when the task is not in the document's TimeLiner tree.</summary>
    private static System.Collections.ObjectModel.Collection<int>? TryGetIndexPath(
        DocumentTimeliner timeliner, TimelinerTask task)
    {
        try
        {
            return timeliner.TaskCreateIndexPath(task);
        }
        catch (Exception ex)
        {
            if (ClashHelpers.IsDisposed(ex))
            {
                throw new InvalidOperationException(
                    "The wired TimeLiner task is stale — Navisworks disposed it when the schedule was last edited. " +
                    "Re-fetch it from TimeLiner.Tasks between edits.", ex);
            }

            return null;
        }
    }

    /// <summary>
    /// Commits an edited copy over a stored task via the document part's TaskEdit (locating the
    /// task by index path, so nested subtasks work too) and returns the stored, updated instance.
    /// Same pattern as TimelinerTask.SetDates.
    /// </summary>
    private static TimelinerTask CommitTaskEdit(Document doc, TimelinerTask storedTask, TimelinerTask editedCopy)
    {
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var path = TryGetIndexPath(timeliner, storedTask);
        if (path == null || path.Count == 0)
        {
            throw new ArgumentException(
                "The task '" + storedTask.DisplayName + "' is not stored in the document. " +
                "Wire a stored task from TimeLiner.Tasks or TimelinerTask.Create.");
        }

        if (path.Count == 1)
        {
            timeliner.TaskEdit(path[0], editedCopy);
        }
        else
        {
            var parentPath = new List<int>(path);
            parentPath.RemoveAt(parentPath.Count - 1);
            var parent = timeliner.TaskResolveIndexPath(parentPath);
            timeliner.TaskEdit(parent, path[path.Count - 1], editedCopy);
        }

        return timeliner.TaskResolveIndexPath(path);
    }
}
