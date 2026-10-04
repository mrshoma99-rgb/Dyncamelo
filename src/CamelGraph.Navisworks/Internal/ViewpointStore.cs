using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// The one place that puts a saved viewpoint into the Saved Viewpoints window: it finds (or creates, on demand) the folder, replaces
/// a same-named viewpoint in it or adds a new one, and hands back the STORED instance (AddCopy and ReplaceWithCopy store copies).
/// <c>Viewpoint.Save</c>, <c>FallHazard.FloorOpeningMap</c> and <c>Viewpoints.ImportFile</c> share it; the folder text is the same
/// everywhere: empty = the top level, a name = a top-level folder, "A/B" = folder B inside folder A. Internal, never surfaced as
/// nodes.
/// </summary>
internal static class ViewpointStore
{
    /// <summary>
    /// The stored folder for a folder text, creating the folders that do not exist yet. Null for an empty text (the top level).
    /// </summary>
    /// <param name="tree">The document's saved viewpoints part.</param>
    /// <param name="folder">Empty, a folder name, or a path such as <c>Reviews/Week 12</c>.</param>
    internal static FolderItem? ResolveFolder(DocumentSavedViewpoints tree, string? folder)
    {
        return ResolveFolder(tree, SavedItemPath.Split(folder));
    }

    /// <summary>
    /// The stored folder for a list of folder names (outermost first), creating the folders that do not exist yet. Null for an empty
    /// list (the top level).
    /// </summary>
    /// <param name="tree">The document's saved viewpoints part.</param>
    /// <param name="segments">The folder names, outermost first.</param>
    internal static FolderItem? ResolveFolder(DocumentSavedViewpoints tree, System.Collections.Generic.IEnumerable<string> segments)
    {
        FolderItem? current = null;
        foreach (var segment in segments)
        {
            current = SavedItemTreeNodesShared.FindOrCreateFolder(
                tree.RootItem,
                current,
                segment,
                item => tree.AddCopy(item),
                (parent, item) => tree.AddCopy(parent, item),
                "viewpoint");
        }

        return current;
    }

    /// <summary>
    /// Stores a viewpoint under its display name in a folder: a viewpoint of that name already there is replaced (so re-running a
    /// graph updates instead of piling up), anything else is added at the end. A same-named folder or animation is never replaced.
    /// </summary>
    /// <param name="tree">The document's saved viewpoints part.</param>
    /// <param name="saved">The viewpoint to store (its DisplayName is the name).</param>
    /// <param name="folder">The stored folder (from <see cref="ResolveFolder"/>), or null for the top level.</param>
    /// <returns>The stored viewpoint.</returns>
    internal static SavedViewpoint Put(DocumentSavedViewpoints tree, SavedViewpoint saved, FolderItem? folder)
    {
        var name = saved.DisplayName;
        var children = folder != null ? folder.Children : tree.Value;
        var existingIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        if (existingIndex >= 0)
        {
            if (folder != null)
            {
                tree.ReplaceWithCopy(folder, existingIndex, saved);
            }
            else
            {
                tree.ReplaceWithCopy(existingIndex, saved);
            }
        }
        else if (folder != null)
        {
            tree.AddCopy(folder, saved);
        }
        else
        {
            tree.AddCopy(saved);
        }

        // AddCopy/ReplaceWithCopy store a copy: hand the stored instance downstream.
        children = folder != null ? folder.Children : tree.Value;
        var storedIndex = NavisValues.FindTopLevelIndex<SavedViewpoint>(children, name);
        return storedIndex >= 0 ? (SavedViewpoint)children[storedIndex] : saved;
    }
}
