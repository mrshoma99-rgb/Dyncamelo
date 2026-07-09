using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
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
        var viewpoints = doc.SavedViewpoints;
        var existingIndex = viewpoints.Value.IndexOfDisplayName(name);
        if (existingIndex >= 0)
        {
            viewpoints.ReplaceWithCopy(existingIndex, saved);
        }
        else
        {
            viewpoints.AddCopy(saved);
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        var storedIndex = viewpoints.Value.IndexOfDisplayName(name);
        return storedIndex >= 0 ? (SavedViewpoint)viewpoints.Value[storedIndex] : saved;
    }
}
