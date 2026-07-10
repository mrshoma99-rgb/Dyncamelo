// NOTE: This file compiles against the Autodesk.Navisworks.Timeliner.dll from the
// Chuongmep.Navis.Api.Autodesk.Navisworks.Timeliner 2023.0.7 package because no
// 2024 Timeliner assembly is published on NuGet. The public API surface is the
// same in 2024, but the reference assembly IS strong-named with Version
// 20.0.1399.50 (PublicKeyToken d85e58fa5af9b484) while Navisworks Manage 2024
// ships 21.0.x, so the host copy does NOT bind automatically: Dyncamelo.App
// (DyncameloHost's static constructor) installs an AppDomain.AssemblyResolve
// handler that redirects the reference to the host's loaded Timeliner assembly
// by simple name. Any other host that loads this library must do the same.
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks.TimeLiner;

/// <summary>Nodes for reading and creating TimeLiner tasks.</summary>
[NodeCategory("Navisworks.TimeLiner")]
public static class TimelinerNodes
{
    /// <summary>All TimeLiner tasks in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every task, with the task hierarchy flattened (subtasks included).</returns>
    [NodeName("TimeLiner.Tasks")]
    [NodeDescription("All TimeLiner tasks in a document, with subtasks flattened into one list.")]
    [NodeSearchTags("timeliner", "tasks", "schedule", "4d", "all")]
    [return: NodeName("tasks")]
    public static List<TimelinerTask> Tasks(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var tasks = new List<TimelinerTask>();
        CollectTasks(timeliner.Tasks, tasks);
        return tasks;
    }

    /// <summary>Summary information about a TimeLiner task.</summary>
    /// <param name="task">The TimeLiner task.</param>
    /// <returns>Name, id, planned/actual dates, task type and progress.</returns>
    [NodeName("TimelinerTask.Info")]
    [NodeDescription("Name, id, planned and actual dates, task type and progress of a TimeLiner task.")]
    [NodeSearchTags("timeliner", "task", "info", "dates", "schedule")]
    [MultiReturn("name", "displayId", "plannedStart", "plannedEnd", "actualStart", "actualEnd", "taskType", "progress")]
    public static Dictionary<string, object?> Info(TimelinerTask task)
    {
        var timelinerTask = RequireTask(task);
        return new Dictionary<string, object?>
        {
            ["name"] = timelinerTask.DisplayName,
            ["displayId"] = timelinerTask.DisplayId,
            ["plannedStart"] = timelinerTask.PlannedStartDate,
            ["plannedEnd"] = timelinerTask.PlannedEndDate,
            ["actualStart"] = timelinerTask.ActualStartDate,
            ["actualEnd"] = timelinerTask.ActualEndDate,
            ["taskType"] = timelinerTask.SimulationTaskTypeName,
            ["progress"] = timelinerTask.ProgressPercent,
        };
    }

    /// <summary>The model items attached to a TimeLiner task.</summary>
    /// <param name="task">The TimeLiner task.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The attached items (search/set attachments are evaluated).</returns>
    [NodeName("TimelinerTask.Items")]
    [NodeDescription("The model items attached to a TimeLiner task. Search and set attachments are re-evaluated.")]
    [NodeSearchTags("timeliner", "task", "items", "attached", "selection")]
    [return: NodeName("items")]
    public static List<ModelItem> Items(TimelinerTask task, Document? document = null)
    {
        var timelinerTask = RequireTask(task);
        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.ToItemList(timelinerTask.Selection.GetSelectedItems(doc));
    }

    /// <summary>Creates a top-level TimeLiner task and optionally attaches items.</summary>
    /// <param name="name">Display name for the new task.</param>
    /// <param name="plannedStart">Planned start date.</param>
    /// <param name="plannedEnd">Planned end date.</param>
    /// <param name="items">Model items to attach (optional).</param>
    /// <param name="taskType">Simulation task type name: "Construct", "Demolish" or "Temporary".</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored TimeLiner task.</returns>
    [NodeName("TimelinerTask.Create")]
    [NodeDescription("Creates a top-level TimeLiner task with planned dates and optionally attaches model items.")]
    [NodeSearchTags("timeliner", "task", "create", "new", "schedule", "4d")]
    [return: NodeName("task")]
    public static TimelinerTask Create(
        string name,
        DateTime plannedStart,
        DateTime plannedEnd,
        IEnumerable<ModelItem>? items = null,
        string taskType = "Construct",
        Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No task name provided.", nameof(name));
        }

        if (plannedEnd < plannedStart)
        {
            throw new ArgumentException("The planned end date is before the planned start date.", nameof(plannedEnd));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var task = new TimelinerTask
        {
            DisplayName = name,
            PlannedStartDate = plannedStart,
            PlannedEndDate = plannedEnd,
            SimulationTaskTypeName = taskType ?? "Construct",
        };

        if (items != null)
        {
            task.Selection.CopyFrom(NavisValues.ToItemList(items));
        }

        timeliner.TaskAddCopy(task);

        // TaskAddCopy stores a copy — hand the stored instance downstream.
        var index = timeliner.Tasks.Count - 1;
        return index >= 0 && timeliner.Tasks[index] is TimelinerTask stored ? stored : task;
    }

    /// <summary>Attaches a saved selection/search set to a TimeLiner task.</summary>
    /// <param name="task">The stored TimeLiner task (from TimeLiner.Tasks or TimelinerTask.Create).</param>
    /// <param name="setName">The saved set's display name (folders are searched too).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored task. Lace over task/set-name lists for bulk 4D linking.</returns>
    [NodeName("TimelinerTask.AttachSet")]
    [NodeDescription("Attaches a saved selection/search set to a task as a LIVE link (like Attach Set in the UI) — the core 4D-linking automation.")]
    [NodeSearchTags("timeliner", "task", "attach", "set", "link", "4d")]
    [return: NodeName("task")]
    public static TimelinerTask AttachSet(TimelinerTask task, string setName, Document? document = null)
    {
        var timelinerTask = RequireTask(task);
        if (string.IsNullOrEmpty(setName))
        {
            throw new ArgumentException("No selection set name provided.", nameof(setName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var set = NavisValues.FindSavedItemByName<SelectionSet>(doc.SelectionSets.RootItem.Children, setName)
            ?? throw new InvalidOperationException(
                "No selection set named '" + setName + "' exists in the document.");

        // A selection SOURCE keeps the attachment linked to the set (the UI's
        // "Attach Set"), so the task follows the set as the model changes.
        var source = doc.SelectionSets.CreateSelectionSource(set);
        var selection = new Selection();
        selection.SelectionSources.Add(source);

        var copy = timelinerTask.CreateCopy();
        copy.Selection.CopyFrom(selection);
        return CommitTaskEdit(doc, timelinerTask, copy);
    }

    /// <summary>Updates the planned dates of a TimeLiner task.</summary>
    /// <param name="task">The stored TimeLiner task.</param>
    /// <param name="plannedStart">New planned start date.</param>
    /// <param name="plannedEnd">New planned end date.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored task. Lace over lists for bulk schedule updates.</returns>
    [NodeName("TimelinerTask.SetDates")]
    [NodeDescription("Updates a task's planned start/end dates in place — bulk schedule edits without a re-import.")]
    [NodeSearchTags("timeliner", "task", "dates", "schedule", "update", "planned")]
    [return: NodeName("task")]
    public static TimelinerTask SetDates(
        TimelinerTask task,
        DateTime plannedStart,
        DateTime plannedEnd,
        Document? document = null)
    {
        var timelinerTask = RequireTask(task);
        if (plannedEnd < plannedStart)
        {
            throw new ArgumentException("The planned end date is before the planned start date.", nameof(plannedEnd));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var copy = timelinerTask.CreateCopy();
        copy.PlannedStartDate = plannedStart;
        copy.PlannedEndDate = plannedEnd;
        return CommitTaskEdit(doc, timelinerTask, copy);
    }

    /// <summary>
    /// Commits an edited copy over a stored task via the document part's TaskEdit
    /// (locating the task by index path, so nested subtasks work too) and returns
    /// the stored, updated instance.
    /// </summary>
    private static TimelinerTask CommitTaskEdit(Document doc, TimelinerTask storedTask, TimelinerTask editedCopy)
    {
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var path = timeliner.TaskCreateIndexPath(storedTask);
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

    private static void CollectTasks(IEnumerable<SavedItem> items, List<TimelinerTask> tasks)
    {
        foreach (var item in items)
        {
            if (item is TimelinerTask task)
            {
                tasks.Add(task);
                CollectTasks(task.Children, tasks); // subtasks
            }
            else if (item is GroupItem group)
            {
                CollectTasks(group.Children, tasks);
            }
        }
    }

    private static TimelinerTask RequireTask(TimelinerTask? task)
    {
        return task ?? throw new ArgumentNullException(nameof(task), "No TimeLiner task provided.");
    }
}
