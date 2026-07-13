using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for saved viewpoints.</summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointNodes
{
    /// <summary>All saved viewpoints in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every saved viewpoint, including those nested in folders (animations are skipped).</returns>
    [NodeName("Viewpoints.All")]
    [NodeDescription("All saved viewpoints in a document, including those inside folders.")]
    [NodeSearchTags("viewpoints", "views", "saved", "camera", "all")]
    [return: NodeName("viewpoints")]
    public static List<SavedViewpoint> All(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        return NavisValues.FlattenSavedItems<SavedViewpoint>(doc.SavedViewpoints.RootItem.Children);
    }

    /// <summary>Finds a saved viewpoint by display name.</summary>
    /// <param name="name">The viewpoint's display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("SavedViewpoint.ByName")]
    [NodeDescription("Finds a saved viewpoint by its display name (searches folders too).")]
    [NodeSearchTags("viewpoint", "view", "byname", "find", "camera")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint ByName(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = NavisValues.FindSavedItemByName<SavedViewpoint>(doc.SavedViewpoints.RootItem.Children, name);
        return viewpoint ?? throw new InvalidOperationException(
            "No saved viewpoint named '" + name + "' exists in the document.");
    }

    /// <summary>Applies a saved viewpoint to the current view.</summary>
    /// <param name="viewpoint">The saved viewpoint to apply.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The applied viewpoint (pass-through).</returns>
    [NodeName("SavedViewpoint.Apply")]
    [NodeDescription("Makes a saved viewpoint the current view (camera, plus any saved overrides).")]
    [NodeSearchTags("viewpoint", "view", "apply", "goto", "camera")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Apply(SavedViewpoint viewpoint, Document? document = null)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No saved viewpoint provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        doc.SavedViewpoints.CurrentSavedViewpoint = viewpoint;
        return viewpoint;
    }

    /// <summary>Saves the current view as a new saved viewpoint.</summary>
    /// <param name="name">Display name for the new viewpoint.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("Viewpoint.SaveCurrent")]
    [NodeDescription("Saves the current view as a new saved viewpoint. An existing top-level viewpoint with the same name is replaced.")]
    [NodeSearchTags("viewpoint", "view", "save", "capture", "camera")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint SaveCurrent(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var saved = new SavedViewpoint(doc.CurrentViewpoint.ToViewpoint()) { DisplayName = name };

        // Re-running a graph should update the viewpoint, not pile up duplicates.
        // The lookup is type-aware so a same-named folder or animation is never
        // silently replaced — only an existing top-level VIEWPOINT is.
        var viewpoints = doc.SavedViewpoints;
        var existingIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(viewpoints.Value, name);
        if (existingIndex >= 0)
        {
            viewpoints.ReplaceWithCopy(existingIndex, saved);
        }
        else
        {
            viewpoints.AddCopy(saved);
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(viewpoints.Value, name);
        return storedIndex >= 0 ? (SavedViewpoint)viewpoints.Value[storedIndex] : saved;
    }

    /// <summary>Saves the current view WITH its appearance/visibility overrides baked in.</summary>
    /// <param name="name">Display name for the new viewpoint.</param>
    /// <param name="folderName">Viewpoint folder to file it under (null/empty stores it at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored saved viewpoint.</returns>
    [NodeName("Viewpoint.SaveWithOverrides")]
    [NodeDescription(
        "Saves the current view AND the current temporary color/transparency/hidden overrides into the " +
        "viewpoint (Navisworks CaptureRuntimeOverrides) — so recalling it restores that exact look, frozen " +
        "as created. Unlike Viewpoint.SaveCurrent (camera only), each view keeps its own appearance. Use with " +
        "Appearance.OverrideColorTemporary / OverrideTransparencyTemporary. An existing same-named viewpoint is replaced.")]
    [NodeSearchTags("viewpoint", "view", "save", "overrides", "appearance", "capture", "isolate", "color", "freeze")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint SaveWithOverrides(string name, string? folderName = null, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);

        // CaptureRuntimeOverrides snapshots the camera PLUS the current temporary
        // appearance and hidden state, so the viewpoint keeps its own look.
        var saved = doc.SavedViewpoints.CaptureRuntimeOverrides();
        saved.DisplayName = name;

        var viewpoints = doc.SavedViewpoints;

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
        var stored = storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : saved;

        // CaptureRuntimeOverrides keeps the appearance/visibility overrides but not
        // the camera (it leaves a default origin/top view); ReplaceFromCurrentView
        // pulls the current camera into the stored viewpoint, preserving the overrides.
        viewpoints.ReplaceFromCurrentView(stored);
        storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        return storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : stored;
    }

    /// <summary>The display name of a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint.</param>
    /// <returns>The viewpoint's display name.</returns>
    [NodeName("SavedViewpoint.Name")]
    [NodeDescription("The display name of a saved viewpoint.")]
    [NodeSearchTags("viewpoint", "view", "name", "displayname")]
    [return: NodeName("name")]
    public static string Name(SavedViewpoint viewpoint)
    {
        if (viewpoint == null)
        {
            throw new ArgumentNullException(nameof(viewpoint), "No saved viewpoint provided.");
        }

        return viewpoint.DisplayName ?? string.Empty;
    }

    /// <summary>Deletes a saved viewpoint by display name.</summary>
    /// <param name="name">The viewpoint's display name (folders are searched too).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when a viewpoint was deleted; false when no viewpoint has that name.</returns>
    [NodeName("SavedViewpoint.Delete")]
    [NodeDescription("Deletes a saved viewpoint by name (searches folders too). Returns false when absent — safe for clean re-runs of batch generation.")]
    [NodeSearchTags("viewpoint", "view", "delete", "remove", "clean")]
    [return: NodeName("deleted")]
    public static bool Delete(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No viewpoint name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var viewpoint = NavisValues.FindSavedItemByName<SavedViewpoint>(viewpoints.RootItem.Children, name);
        if (viewpoint == null)
        {
            return false;
        }

        var parent = viewpoint.Parent;
        return parent == null ? viewpoints.Remove(viewpoint) : viewpoints.Remove(parent, viewpoint);
    }

    /// <summary>Creates one saved viewpoint per clash result, aimed at the clash.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="folderName">Viewpoint folder to file them under (null/empty stores them at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored viewpoints, one per result, named after the result.</returns>
    [NodeName("Viewpoints.FromClashResults")]
    [NodeDescription("Batch-generates one saved viewpoint per clash result, camera aimed at the clash and named after the result — the clash-triage staple. Existing same-named viewpoints in the folder are replaced.")]
    [NodeSearchTags("viewpoints", "clash", "results", "batch", "generate", "triage")]
    [return: NodeName("viewpoints")]
    public static List<SavedViewpoint> FromClashResults(
        IEnumerable<ClashResult> results,
        string? folderName = "Clash Views",
        Document? document = null)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var viewpoints = doc.SavedViewpoints;

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

        var stored = new List<SavedViewpoint>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            var camera = clash.TestsData.TestsViewpointForResult(result);
            var name = string.IsNullOrEmpty(result.DisplayName) ? "Clash" : result.DisplayName;
            var saved = new SavedViewpoint(camera) { DisplayName = name };

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
            stored.Add(storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : saved);
        }

        return stored;
    }
}
