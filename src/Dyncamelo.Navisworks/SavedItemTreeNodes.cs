using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Saved-viewpoint tree operations: rename, folders, move, folder read-back
/// (wishlist #1). All edits go through <c>DocumentSavedViewpoints</c> part
/// methods on the STORED items — stored saved items are read-only and are never
/// edited in place.
/// </summary>
[NodeCategory("Navisworks.Viewpoints")]
public static class ViewpointTreeNodes
{
    /// <summary>Renames a saved viewpoint.</summary>
    /// <param name="viewpoint">The saved viewpoint, or its current display name.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The renamed stored viewpoint (pass-through for chaining).</returns>
    [NodeName("SavedViewpoint.Rename")]
    [NodeDescription("Renames a saved viewpoint (accepts the viewpoint or its current name; searches folders too). Batch-rename via lacing.")]
    [NodeSearchTags("viewpoint", "view", "rename", "name", "batch")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Rename(object viewpoint, string newName, Document? document = null)
    {
        if (string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("No new viewpoint name provided.", nameof(newName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, viewpoint, "saved viewpoint");
        viewpoints.EditDisplayName(stored, newName);
        return stored;
    }

    /// <summary>Creates (or reuses) a viewpoint folder, optionally nested.</summary>
    /// <param name="name">The folder's display name.</param>
    /// <param name="parentFolder">Parent folder for nesting (null creates at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored folder (an existing same-named folder in that location is reused).</returns>
    [NodeName("Viewpoints.CreateFolder")]
    [NodeDescription("Creates a folder in the Saved Viewpoints window, optionally nested under a parent folder. An existing same-named folder in that location is reused, so re-runs are clean.")]
    [NodeSearchTags("viewpoints", "folder", "create", "organize", "nested")]
    [return: NodeName("folder")]
    public static FolderItem CreateFolder(string name, FolderItem? parentFolder = null, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No folder name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var storedParent = parentFolder == null
            ? null
            : SavedItemTreeHelpers.ResolveStored<FolderItem>(viewpoints.RootItem, parentFolder, "viewpoint folder");

        return SavedItemTreeNodesShared.FindOrCreateFolder(
            viewpoints.RootItem,
            storedParent,
            name,
            item => viewpoints.AddCopy(item),
            (parent, item) => viewpoints.AddCopy(parent, item),
            "viewpoint");
    }

    /// <summary>Moves a stored viewpoint into a folder.</summary>
    /// <param name="viewpoint">The saved viewpoint, or its display name.</param>
    /// <param name="folder">The target folder (e.g. from Viewpoints.CreateFolder), or its name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved stored viewpoint (pass-through for chaining).</returns>
    [NodeName("SavedViewpoint.MoveToFolder")]
    [NodeDescription("Moves a saved viewpoint into a folder (appended at the end). A viewpoint already in the folder is left alone, so re-runs are clean.")]
    [NodeSearchTags("viewpoint", "view", "move", "folder", "organize")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint MoveToFolder(object viewpoint, object folder, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, viewpoint, "saved viewpoint");
        var storedFolder = SavedItemTreeHelpers.ResolveStored<FolderItem>(
            viewpoints.RootItem, folder, "viewpoint folder");

        return SavedItemTreeHelpers.MoveToFolder(
            viewpoints.RootItem,
            stored,
            storedFolder,
            (oldParent, oldIndex, newParent, newIndex) => viewpoints.Move(oldParent, oldIndex, newParent, newIndex),
            "saved viewpoint");
    }

    /// <summary>Reads a viewpoint's containing folder.</summary>
    /// <param name="viewpoint">The saved viewpoint, or its display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The folder path ("A/B"; "" at the top level) and the immediate folder (null at the top level).</returns>
    [NodeName("SavedViewpoint.Folder")]
    [NodeDescription("The folder containing a saved viewpoint: its path as \"A/B\" (\"\" for top-level viewpoints) and the folder itself — drives folder-based status workflows.")]
    [NodeSearchTags("viewpoint", "view", "folder", "path", "parent", "location")]
    [MultiReturn("folderPath", "folder")]
    public static Dictionary<string, object?> Folder(object viewpoint, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, viewpoint, "saved viewpoint");
        SavedItemTreeHelpers.GetFolderInfo(viewpoints.RootItem, stored, out var folderPath, out var folder);

        return new Dictionary<string, object?>
        {
            ["folderPath"] = folderPath,
            ["folder"] = folder,
        };
    }
}

/// <summary>
/// Selection-set tree operations: rename, nested folders, move (wishlist #1).
/// All edits go through <c>DocumentSelectionSets</c> part methods on the STORED
/// items — stored saved items are read-only and are never edited in place.
/// </summary>
[NodeCategory("Navisworks.SelectionSets")]
public static class SelectionSetTreeNodes
{
    /// <summary>Renames a saved selection or search set.</summary>
    /// <param name="selectionSet">The set, or its current display name.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The renamed stored set (pass-through for chaining).</returns>
    [NodeName("SelectionSet.Rename")]
    [NodeDescription("Renames a saved selection or search set (accepts the set or its current name; searches folders too). Batch-rename via lacing.")]
    [NodeSearchTags("selection", "set", "rename", "name", "batch")]
    [return: NodeName("selectionSet")]
    public static SelectionSet Rename(object selectionSet, string newName, Document? document = null)
    {
        if (string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("No new selection set name provided.", nameof(newName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var sets = doc.SelectionSets;
        var stored = SavedItemTreeHelpers.ResolveStored<SelectionSet>(
            sets.RootItem, selectionSet, "selection set");
        sets.EditDisplayName(stored, newName);
        return stored;
    }

    /// <summary>Creates (or reuses) a sets folder, optionally nested.</summary>
    /// <param name="name">The folder's display name.</param>
    /// <param name="parentFolder">Parent folder for nesting (null creates at the top level).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored folder (an existing same-named folder in that location is reused).</returns>
    /// <remarks>
    /// v0.3 extension of the v0.2 SelectionSets.CreateFolder node (adds the
    /// parentFolder input). This is now the sole definition — the v0.2 method
    /// was removed from SelectionSetNodes.cs during v0.3 integration.
    /// </remarks>
    [NodeName("SelectionSets.CreateFolder")]
    [NodeDescription("Creates a folder in the Sets window, optionally nested under a parent folder. An existing same-named folder in that location is reused, so re-runs are clean.")]
    [NodeSearchTags("selection", "sets", "folder", "create", "organize", "nested")]
    [return: NodeName("folder")]
    public static FolderItem CreateFolder(string name, FolderItem? parentFolder = null, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No folder name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var sets = doc.SelectionSets;
        var storedParent = parentFolder == null
            ? null
            : SavedItemTreeHelpers.ResolveStored<FolderItem>(sets.RootItem, parentFolder, "sets folder");

        return SavedItemTreeNodesShared.FindOrCreateFolder(
            sets.RootItem,
            storedParent,
            name,
            item => sets.AddCopy(item),
            (parent, item) => sets.AddCopy(parent, item),
            "sets");
    }

    /// <summary>Moves a stored set into a folder.</summary>
    /// <param name="selectionSet">The set, or its display name.</param>
    /// <param name="folder">The target folder (e.g. from SelectionSets.CreateFolder), or its name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The moved stored set (pass-through for chaining).</returns>
    [NodeName("SelectionSet.MoveToFolder")]
    [NodeDescription("Moves a saved selection or search set into a folder (appended at the end). A set already in the folder is left alone, so re-runs are clean.")]
    [NodeSearchTags("selection", "set", "move", "folder", "organize")]
    [return: NodeName("selectionSet")]
    public static SelectionSet MoveToFolder(object selectionSet, object folder, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var sets = doc.SelectionSets;
        var stored = SavedItemTreeHelpers.ResolveStored<SelectionSet>(
            sets.RootItem, selectionSet, "selection set");
        var storedFolder = SavedItemTreeHelpers.ResolveStored<FolderItem>(
            sets.RootItem, folder, "sets folder");

        return SavedItemTreeHelpers.MoveToFolder(
            sets.RootItem,
            stored,
            storedFolder,
            (oldParent, oldIndex, newParent, newIndex) => sets.Move(oldParent, oldIndex, newParent, newIndex),
            "selection set");
    }
}

/// <summary>
/// Folder find-or-create shared by the viewpoint and selection-set node classes
/// (the two document parts have identical surfaces but no common base type, so
/// the part-specific AddCopy overloads come in as delegates).
/// </summary>
internal static class SavedItemTreeNodesShared
{
    /// <summary>
    /// Finds a same-named folder among the target location's direct children or
    /// creates a new one there, and returns the STORED folder instance.
    /// </summary>
    /// <param name="root">The tree root (<c>part.RootItem</c>).</param>
    /// <param name="storedParent">The stored parent folder, or null for the top level.</param>
    /// <param name="name">The folder display name.</param>
    /// <param name="addTopLevel">The part's <c>AddCopy(SavedItem)</c>.</param>
    /// <param name="addNested">The part's <c>AddCopy(GroupItem, SavedItem)</c>.</param>
    /// <param name="treeLabel">Human label for error messages (e.g. "viewpoint").</param>
    internal static FolderItem FindOrCreateFolder(
        FolderItem root,
        FolderItem? storedParent,
        string name,
        Action<SavedItem> addTopLevel,
        Action<GroupItem, SavedItem> addNested,
        string treeLabel)
    {
        var children = storedParent != null ? storedParent.Children : root.Children;
        var index = NavisValues.FindTopLevelIndex<FolderItem>(children, name);
        if (index < 0)
        {
            var folder = new FolderItem { DisplayName = name };
            if (storedParent != null)
            {
                addNested(storedParent, folder);
            }
            else
            {
                addTopLevel(folder);
            }

            // AddCopy stores a copy — re-fetch the stored instance.
            children = storedParent != null ? storedParent.Children : root.Children;
            index = NavisValues.FindTopLevelIndex<FolderItem>(children, name);
        }

        if (index < 0)
        {
            throw new InvalidOperationException("Could not create the " + treeLabel + " folder '" + name + "'.");
        }

        return (FolderItem)children[index];
    }
}
