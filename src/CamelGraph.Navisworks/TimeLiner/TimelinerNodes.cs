// NOTE: This file compiles against the Autodesk.Navisworks.Timeliner.dll from the
// Chuongmep.Navis.Api.Autodesk.Navisworks.Timeliner 2023.0.7 package because no
// 2024 Timeliner assembly is published on NuGet. The public API surface is the
// same in 2024, but the reference assembly IS strong-named with Version
// 20.0.1399.50 (PublicKeyToken d85e58fa5af9b484) while Navisworks Manage 2024
// ships 21.0.x, so the host copy does NOT bind automatically: CamelGraph.App
// (CamelGraphHost's static constructor) installs an AppDomain.AssemblyResolve
// handler that redirects the reference to the host's loaded Timeliner assembly
// by simple name. Any other host that loads this library must do the same.
using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Timeliner;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks.TimeLiner;

/// <summary>Nodes for reading and creating TimeLiner tasks.</summary>
[NodeCategory("Navisworks.TimeLiner")]
public static class TimelinerNodes
{
    /// <summary>All TimeLiner tasks in a document.</summary>
    /// <param name="order">"parents first" (default) lists each task before its subtasks; "children first" lists the subtasks before their task.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every task, with the task hierarchy flattened (subtasks included).</returns>
    [NodeName("TimeLiner.Tasks")]
    [LiveState]
    [NodeAliases("CamelGraph.Navisworks.TimeLiner.TimelinerNodes.Tasks@Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "All TimeLiner tasks in a document, with subtasks flattened into one list, read again on every run. Each task comes before its subtasks " +
        "(\"parents first\"). Navisworks replaces a task's subtasks when the task itself is edited, so when you edit or delete a whole list that " +
        "holds tasks together with their subtasks (TimelinerTask.SetProgress, SetDates, SetActual, Delete over the list), set order to " +
        "\"children first\" in the Advanced panel: the subtasks are then handled before their task and none is already replaced when its turn comes.")]
    [NodeSearchTags("timeliner", "tasks", "schedule", "4d", "all", "timelinertask", "timeliner task")]
    [return: NodeName("tasks")]
    public static List<TimelinerTask> Tasks(
        [NodePanel("Advanced")] [NodeChoices("parents first", "children first")] string order = "parents first",
        Document? document = null)
    {
        var childrenFirst = ParseOrder(order);
        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        var tasks = new List<TimelinerTask>();
        CollectTasks(timeliner.Tasks, tasks, childrenFirst);
        return tasks;
    }

    /// <summary>Finds a TimeLiner task by its name.</summary>
    /// <param name="name">The task's name, or its path through the parent tasks such as "Structure/Level 2".</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored task.</returns>
    [NodeName("TimelinerTask.ByName")]
    [LiveState]
    [NodeDescription(
        "Finds a TimeLiner task by its name (subtasks too). When several tasks share the name the first one in the list is used and the node shows " +
        "a warning; give the path through the parent tasks (\"Structure/Level 2\") to choose one exactly. Fails when there is no such task.")]
    [NodeSearchTags("timeliner", "task", "byname", "find", "lookup", "schedule", "4d", "path")]
    [return: NodeName("task")]
    public static TimelinerTask ByName(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No task name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");
        return FindTaskByNameOrPath(timeliner, name)
            ?? throw new InvalidOperationException("No TimeLiner task named '" + name + "' exists in the document.");
    }

    /// <summary>Summary information about a TimeLiner task.</summary>
    /// <param name="task">The TimeLiner task.</param>
    /// <returns>Name, id, planned/actual dates, task type, progress, the parent task's name and the depth in the hierarchy.</returns>
    [NodeName("TimelinerTask.Info")]
    [NodeDescription("Name, id, planned and actual dates, task type and progress of a TimeLiner task, the name of its parent task (empty for a top-level task) and its depth (0 for a top-level task, 1 for a subtask ...).")]
    [NodeSearchTags("timeliner", "task", "info", "dates", "schedule", "parent", "depth", "hierarchy")]
    [MultiReturn("name", "displayId", "plannedStart", "plannedEnd", "actualStart", "actualEnd", "taskType", "progress", "parent", "depth")]
    [PortKinds("text", "text", "datetime", "datetime", "datetime", "datetime", "text", "number", "text", "integer")]
    public static Dictionary<string, object?> Info(TimelinerTask task)
    {
        var timelinerTask = RequireTask(task);
        var parentName = string.Empty;
        var depth = 0;
        var ancestor = timelinerTask.Parent;
        while (ancestor is TimelinerTask parentTask)
        {
            if (depth == 0)
            {
                parentName = parentTask.DisplayName ?? string.Empty;
            }

            depth++;
            ancestor = parentTask.Parent;
        }

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
            ["parent"] = parentName,
            ["depth"] = depth,
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

    /// <summary>Creates a TimeLiner task, or updates the task of that name, and optionally attaches items.</summary>
    /// <param name="name">Display name for the task.</param>
    /// <param name="plannedStart">Planned start date.</param>
    /// <param name="plannedEnd">Planned end date.</param>
    /// <param name="items">Model items to attach (optional).</param>
    /// <param name="taskType">Simulation task type: Construct, Demolish or Temporary (a custom task type of the document can be wired in as text).</param>
    /// <param name="parent">The task to create this one under: a task, its name or its path such as "Structure/Level 2". Empty creates a top-level task.</param>
    /// <param name="onExisting">What to do when the same parent already has a task of this name: "update" changes that task (dates, type, items), "add" always adds another, "error" stops.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored TimeLiner task.</returns>
    [NodeName("TimelinerTask.Create")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.TimeLiner.TimelinerNodes.Create@string,System.DateTime,System.DateTime,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Creates a TimeLiner task with planned dates and optionally attaches model items. Leave parent empty for a top-level task, or give the " +
        "parent task (TimelinerTask.ByName, or its name or path) to build a schedule hierarchy. A task of the same name under the same parent is " +
        "updated instead of added again, so running the graph twice does not duplicate the schedule; set onExisting (Advanced) to \"add\" to always " +
        "add one or to \"error\" to stop.")]
    [NodeSearchTags("timeliner", "task", "create", "new", "schedule", "4d", "subtask", "parent", "hierarchy")]
    [return: NodeName("task")]
    public static TimelinerTask Create(
        string name,
        DateTime plannedStart,
        DateTime plannedEnd,
        IEnumerable<ModelItem>? items = null,
        [NodeChoices("Construct", "Demolish", "Temporary")] string taskType = "Construct",
        [ScalarInput] object? parent = null,
        [NodePanel("Advanced")] [NodeChoices("update", "add", "error")] string onExisting = "update",
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

        var mode = ParseOnExisting(onExisting);
        var doc = NavisworksContext.ResolveDocument(document);
        var timeliner = doc.GetTimeliner()
            ?? throw new InvalidOperationException("TimeLiner is not available in this Navisworks edition.");

        TimelinerTask? storedParent = null;
        System.Collections.ObjectModel.Collection<int>? parentPath = null;
        if (parent != null)
        {
            storedParent = ResolveParent(timeliner, parent);
            parentPath = timeliner.TaskCreateIndexPath(storedParent);
        }

        var siblings = storedParent != null ? storedParent.Children : timeliner.Tasks;
        TimelinerTask? existing = null;
        foreach (var sibling in siblings)
        {
            if (sibling is TimelinerTask candidate && string.Equals(candidate.DisplayName, name, StringComparison.Ordinal))
            {
                existing = candidate;
                break;
            }
        }

        if (existing != null && mode != "add")
        {
            if (mode == "error")
            {
                throw new InvalidOperationException(
                    "A task named '" + name + "' already exists" + (storedParent != null ? " under '" + storedParent.DisplayName + "'" : string.Empty) +
                    ". Set onExisting to \"update\" to change it or \"add\" to add another.");
            }

            var edited = existing.CreateCopy();
            edited.PlannedStartDate = plannedStart;
            edited.PlannedEndDate = plannedEnd;
            edited.SimulationTaskTypeName = taskType ?? "Construct";
            if (items != null)
            {
                edited.Selection.CopyFrom(NavisValues.ToItemList(items));
            }

            return CommitTaskEdit(doc, existing, edited);
        }

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

        if (storedParent == null)
        {
            timeliner.TaskAddCopy(task);

            // TaskAddCopy stores a copy — hand the stored instance downstream.
            var index = timeliner.Tasks.Count - 1;
            return index >= 0 && timeliner.Tasks[index] is TimelinerTask stored ? stored : task;
        }

        timeliner.TaskAddCopy(storedParent, task);

        // The parent was replaced by the add: read it again by its place, then take its last child.
        var refreshed = timeliner.TaskResolveIndexPath(parentPath!);
        var last = refreshed.Children.Count - 1;
        return last >= 0 && refreshed.Children[last] is TimelinerTask child ? child : task;
    }

    /// <summary>Attaches a saved selection/search set to a TimeLiner task.</summary>
    /// <param name="task">The stored TimeLiner task (from TimeLiner.Tasks or TimelinerTask.Create).</param>
    /// <param name="set">The saved selection or search set: the set itself (SelectionSet.ByName, SelectionSets.All), its display name or its folder path and name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The updated stored task. Lace over task/set lists for bulk 4D linking.</returns>
    [NodeName("TimelinerTask.AttachSet")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.TimeLiner.TimelinerNodes.AttachSet@Autodesk.Navisworks.Api.Timeliner.TimelinerTask,string,Autodesk.Navisworks.Api.Document")]
    [PortAlias("setName", "set")]
    [NodeDescription(
        "Attaches a saved selection/search set to a task as a LIVE link (like Attach Set in the UI) — the core 4D-linking automation. " +
        "Give the set itself, its name, or its folder path and name; a list of tasks and a list of sets link each task to the set at the same position.")]
    [NodeSearchTags("timeliner", "task", "attach", "set", "link", "4d")]
    [return: NodeName("task")]
    public static TimelinerTask AttachSet(TimelinerTask task, [ScalarInput] object set, Document? document = null)
    {
        var timelinerTask = RequireTask(task);
        if (set == null)
        {
            throw new ArgumentNullException(nameof(set), "No selection set provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var selectionSet = SavedItemTreeHelpers.ResolveStored<SelectionSet>(doc.SelectionSets.RootItem, set, "selection set");

        // A selection SOURCE keeps the attachment linked to the set (the UI's
        // "Attach Set"), so the task follows the set as the model changes.
        var source = doc.SelectionSets.CreateSelectionSource(selectionSet);
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
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Updates a task's planned start/end dates in place — bulk schedule edits without a re-import. Over a list that holds tasks together with their subtasks, set TimeLiner.Tasks to order \"children first\".")]
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

    private static bool ParseOrder(string? order)
    {
        var text = (order ?? string.Empty).Trim();
        if (text.Length == 0 || string.Equals(text, "parents first", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(text, "children first", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new ArgumentException("Unknown order '" + order + "'. Use \"parents first\" or \"children first\".", nameof(order));
    }

    private static string ParseOnExisting(string? onExisting)
    {
        var text = (onExisting ?? string.Empty).Trim().ToLowerInvariant();
        switch (text)
        {
            case "":
            case "update":
                return "update";
            case "add":
            case "error":
                return text;
            default:
                throw new ArgumentException("Unknown onExisting '" + onExisting + "'. Use \"update\", \"add\" or \"error\".", nameof(onExisting));
        }
    }

    /// <summary>The stored task a parent input names: a task, its name or its path.</summary>
    private static TimelinerTask ResolveParent(DocumentTimeliner timeliner, object parent)
    {
        switch (parent)
        {
            case TimelinerTask task:
                if (TryIndexPath(timeliner, task) == null)
                {
                    throw new ArgumentException(
                        "The parent task '" + task.DisplayName + "' is not stored in the document. Wire a stored task from TimeLiner.Tasks, TimelinerTask.ByName or TimelinerTask.Create.", nameof(parent));
                }

                return task;
            case string text:
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("No parent task name provided.", nameof(parent));
                }

                return FindTaskByNameOrPath(timeliner, text.Trim())
                    ?? throw new InvalidOperationException("No TimeLiner task named '" + text.Trim() + "' exists to be the parent.");
            default:
                throw new ArgumentException(
                    "Cannot use a " + parent.GetType().Name + " as the parent task. Wire a task or its name.", nameof(parent));
        }
    }

    private static System.Collections.ObjectModel.Collection<int>? TryIndexPath(DocumentTimeliner timeliner, TimelinerTask task)
    {
        try
        {
            var path = timeliner.TaskCreateIndexPath(task);
            return path != null && path.Count > 0 ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A task by name (the first in the list, with a warning when several share it) or by its path through the parents.</summary>
    internal static TimelinerTask? FindTaskByNameOrPath(DocumentTimeliner timeliner, string text)
    {
        var all = new List<TimelinerTask>();
        CollectTasks(timeliner.Tasks, all, false);

        TimelinerTask? first = null;
        var count = 0;
        foreach (var task in all)
        {
            if (string.Equals(task.DisplayName, text, StringComparison.Ordinal))
            {
                count++;
                first ??= task;
            }
        }

        if (first != null)
        {
            if (count > 1)
            {
                NodeWarnings.Add(
                    count + " TimeLiner tasks are named '" + text + "'; the first one in the list was used. " +
                    "Give the path through the parent tasks (for example \"Structure/" + text + "\") to choose another.");
            }

            return first;
        }

        if (!SavedItemPath.IsPath(text))
        {
            return null;
        }

        IEnumerable<SavedItem> level = timeliner.Tasks;
        TimelinerTask? current = null;
        foreach (var segment in SavedItemPath.Split(text))
        {
            TimelinerTask? next = null;
            foreach (var item in level)
            {
                if (item is TimelinerTask candidate && string.Equals(candidate.DisplayName, segment, StringComparison.Ordinal))
                {
                    next = candidate;
                    break;
                }
            }

            if (next == null)
            {
                return null;
            }

            current = next;
            level = next.Children;
        }

        return current;
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

        System.Collections.ObjectModel.Collection<int>? path;
        try
        {
            path = timeliner.TaskCreateIndexPath(storedTask);
        }
        catch (Exception)
        {
            path = null;
        }

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

    private static void CollectTasks(IEnumerable<SavedItem> items, List<TimelinerTask> tasks, bool childrenFirst)
    {
        foreach (var item in items)
        {
            if (item is TimelinerTask task)
            {
                if (!childrenFirst)
                {
                    tasks.Add(task);
                }

                CollectTasks(task.Children, tasks, childrenFirst); // subtasks

                if (childrenFirst)
                {
                    tasks.Add(task);
                }
            }
            else if (item is GroupItem group)
            {
                CollectTasks(group.Children, tasks, childrenFirst);
            }
        }
    }

    private static TimelinerTask RequireTask(TimelinerTask? task)
    {
        return task ?? throw new ArgumentNullException(nameof(task), "No TimeLiner task provided.");
    }
}
