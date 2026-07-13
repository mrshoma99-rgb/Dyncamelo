using System;
using System.Collections;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Workflow;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Builder nodes that produce reified <see cref="IWorkflowAction"/>s — deferred
/// per-item operations for <c>Workflow.ForEach</c>. Each node returns an action
/// value (no host effect); the effect runs later, once per item, inside the loop.
/// Wire the actions into a List.Create (Isolate, then Zoom To, then Save
/// Viewpoint) and feed that into Workflow.ForEach.
/// </summary>
[NodeCategory("Workflow.Actions")]
public static class WorkflowActionNodes
{
    /// <summary>Builds an action that isolates the current item (shows it, hides everything else).</summary>
    /// <returns>An isolate action for Workflow.ForEach.</returns>
    [NodeName("Action.Isolate")]
    [NodeDescription("Per item: shows the current item and hides everything else (Appearance.Isolate). Place before Save Viewpoint to bake the isolation into each view.")]
    [NodeSearchTags("action", "isolate", "workflow", "foreach", "only", "focus")]
    [return: NodeName("action")]
    public static IWorkflowAction Isolate() => new IsolateAction();

    /// <summary>Builds an action that shows every item (undoes an isolate).</summary>
    /// <returns>A show-all action for Workflow.ForEach.</returns>
    [NodeName("Action.ShowAll")]
    [NodeDescription("Per item: un-hides every item in the model (Appearance.ShowAll) — a reset step, e.g. before re-isolating.")]
    [NodeSearchTags("action", "showall", "workflow", "foreach", "reveal", "reset", "unhide")]
    [return: NodeName("action")]
    public static IWorkflowAction ShowAll() => new ShowAllAction();

    /// <summary>Builds an action that frames the current item in the view.</summary>
    /// <param name="paddingFactor">Space to leave around the item (1 = tight fit).</param>
    /// <returns>A zoom action for Workflow.ForEach.</returns>
    [NodeName("Action.ZoomTo")]
    [NodeDescription("Per item: frames the current item in the view (Camera.ZoomToItems). Place before Save Viewpoint so each view is centred on its item.")]
    [NodeSearchTags("action", "zoom", "workflow", "foreach", "frame", "fit", "focus")]
    [return: NodeName("action")]
    public static IWorkflowAction ZoomTo(double paddingFactor = 1.5) => new ZoomToAction(paddingFactor);

    /// <summary>Builds an action that saves the current view as a named viewpoint.</summary>
    /// <param name="name">Viewpoint name; supports {name}, {index1}, {count} templating (default {name} = the item's name).</param>
    /// <param name="folder">Viewpoint folder to file them under (null/empty stores at the top level).</param>
    /// <param name="bakeVisibility">True to capture the current hidden/appearance state into the viewpoint, so recalling it re-isolates the item.</param>
    /// <returns>A save-viewpoint action that collects the created viewpoint.</returns>
    [NodeName("Action.SaveViewpoint")]
    [NodeDescription("Per item: saves the current view as a saved viewpoint named from the item. With bakeVisibility on, the current isolation is stored in the view, so recalling it re-isolates the item. Collected as the loop's per-item result.")]
    [NodeSearchTags("action", "viewpoint", "view", "save", "workflow", "foreach", "capture", "snapshot")]
    [return: NodeName("action")]
    public static IWorkflowAction SaveViewpoint(
        string name = "{name}",
        string? folder = "Dyncamelo Views",
        bool bakeVisibility = true)
        => new SaveViewpointAction(name, folder, bakeVisibility);

    /// <summary>Builds an action that overrides the current item's color.</summary>
    /// <param name="color">A Color, a "#RRGGBB" string, or a list of three numbers.</param>
    /// <returns>A recolor action for Workflow.ForEach.</returns>
    [NodeName("Action.OverrideColor")]
    [NodeDescription("Per item: overrides the current item's color (Appearance.OverrideColor).")]
    [NodeSearchTags("action", "color", "override", "workflow", "foreach", "paint", "tint")]
    [return: NodeName("action")]
    public static IWorkflowAction OverrideColor(object color) => new OverrideColorAction(color);

    /// <summary>Builds an action that removes color/transparency overrides from the current item.</summary>
    /// <returns>A reset-appearance action for Workflow.ForEach.</returns>
    [NodeName("Action.ResetAppearance")]
    [NodeDescription("Per item: removes color and transparency overrides from the current item (Appearance.Reset).")]
    [NodeSearchTags("action", "reset", "appearance", "workflow", "foreach", "restore", "original")]
    [return: NodeName("action")]
    public static IWorkflowAction ResetAppearance() => new ResetAppearanceAction();

    /// <summary>
    /// Resolves the current iteration's item to model items: a single item, a
    /// collection (ModelItemCollection, a group sub-list), or nested lists thereof.
    /// Throws a graph-visible error when the item is not model geometry.
    /// </summary>
    private static List<ModelItem> CurrentModelItems(WorkflowContext context)
    {
        var items = new List<ModelItem>();
        Collect(context.CurrentItem, items);
        if (items.Count == 0)
        {
            var got = context.CurrentItem?.GetType().Name ?? "null";
            throw new InvalidOperationException(
                "Workflow item #" + context.Index1 + " ('" + context.ItemName + "') is not a model item (got " +
                got + "). Action.Isolate / ZoomTo / SaveViewpoint iterate model items — wire a list of model items into Workflow.ForEach.");
        }

        return items;
    }

    private static void Collect(object? value, List<ModelItem> into)
    {
        if (value is ModelItem item)
        {
            into.Add(item);
        }
        else if (value is IEnumerable sequence && !(value is string))
        {
            foreach (var element in sequence)
            {
                Collect(element, into);
            }
        }
    }

    /// <summary>Isolates the current item.</summary>
    private sealed class IsolateAction : IWorkflowAction
    {
        public string Describe() => "Isolate";

        public void Run(WorkflowContext context) => AppearanceNodes.Isolate(CurrentModelItems(context));
    }

    /// <summary>Shows every item in the model.</summary>
    private sealed class ShowAllAction : IWorkflowAction
    {
        public string Describe() => "Show all";

        public void Run(WorkflowContext context) => AppearanceNodes.ShowAll();
    }

    /// <summary>Frames the current item in the view.</summary>
    private sealed class ZoomToAction : IWorkflowAction
    {
        private readonly double _paddingFactor;

        public ZoomToAction(double paddingFactor) => _paddingFactor = paddingFactor;

        public string Describe() => "Zoom to item";

        public void Run(WorkflowContext context) => CameraNodes.ZoomToItems(CurrentModelItems(context), _paddingFactor);
    }

    /// <summary>Overrides the current item's color.</summary>
    private sealed class OverrideColorAction : IWorkflowAction
    {
        private readonly object _color;

        public OverrideColorAction(object color) => _color = color;

        public string Describe() => "Override color";

        public void Run(WorkflowContext context) => AppearanceNodes.OverrideColor(CurrentModelItems(context), _color);
    }

    /// <summary>Removes color/transparency overrides from the current item.</summary>
    private sealed class ResetAppearanceAction : IWorkflowAction
    {
        public string Describe() => "Reset appearance";

        public void Run(WorkflowContext context) => AppearanceNodes.Reset(CurrentModelItems(context));
    }

    /// <summary>
    /// Saves the current view as a named saved viewpoint, optionally baking the
    /// current isolation/appearance overrides into it, and collects the stored
    /// viewpoint as the iteration's result.
    /// </summary>
    private sealed class SaveViewpointAction : IWorkflowAction
    {
        private readonly string _nameTemplate;
        private readonly string? _folderName;
        private readonly bool _bakeVisibility;

        public SaveViewpointAction(string nameTemplate, string? folderName, bool bakeVisibility)
        {
            _nameTemplate = nameTemplate;
            _folderName = folderName;
            _bakeVisibility = bakeVisibility;
        }

        public string Describe() => "Save viewpoint '" + _nameTemplate + "'";

        public void Run(WorkflowContext context)
        {
            var doc = NavisworksContext.ResolveDocument();
            var name = context.Expand(_nameTemplate);
            if (string.IsNullOrEmpty(name))
            {
                name = "View " + context.Index1;
            }

            var viewpoints = doc.SavedViewpoints;

            // CaptureRuntimeOverrides snapshots the camera PLUS the current hidden
            // and appearance overrides, so a viewpoint saved after an isolate
            // re-isolates its item when recalled. Without baking, only the camera
            // is stored (a plain "look here" view).
            var saved = _bakeVisibility
                ? viewpoints.CaptureRuntimeOverrides()
                : new SavedViewpoint(doc.CurrentViewpoint.ToViewpoint());
            saved.DisplayName = name;

            var stored = StoreViewpoint(viewpoints, saved, name, _folderName);
            context.Collect(stored);
        }
    }

    /// <summary>
    /// Files a saved viewpoint under an optional folder (created on demand),
    /// replacing an existing same-named viewpoint in that scope so re-runs update
    /// rather than duplicate. Returns the stored instance.
    /// </summary>
    private static SavedViewpoint StoreViewpoint(
        Autodesk.Navisworks.Api.DocumentParts.DocumentSavedViewpoints viewpoints,
        SavedViewpoint saved,
        string name,
        string? folderName)
    {
        FolderItem? folder = null;
        if (!string.IsNullOrEmpty(folderName))
        {
            var folderIndex = NavisValues.FindTopLevelIndex<FolderItem>(viewpoints.Value, folderName!);
            if (folderIndex < 0)
            {
                viewpoints.AddCopy(new FolderItem { DisplayName = folderName });
                folderIndex = NavisValues.FindTopLevelIndex<FolderItem>(viewpoints.Value, folderName!);
            }

            folder = folderIndex >= 0 ? (FolderItem)viewpoints.Value[folderIndex] : null;
        }

        var children = folder != null ? folder.Children : viewpoints.Value;
        var existingIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        if (existingIndex >= 0)
        {
            if (folder != null)
            {
                viewpoints.ReplaceWithCopy(folder, existingIndex, saved);
            }
            else
            {
                viewpoints.ReplaceWithCopy(existingIndex, saved);
            }
        }
        else if (folder != null)
        {
            viewpoints.AddCopy(folder, saved);
        }
        else
        {
            viewpoints.AddCopy(saved);
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        return storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : saved;
    }
}
