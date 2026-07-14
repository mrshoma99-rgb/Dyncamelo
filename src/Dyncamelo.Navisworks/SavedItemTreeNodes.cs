using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
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

    /// <summary>Renames a saved-viewpoint folder.</summary>
    /// <param name="folder">The folder (e.g. from Viewpoints.CreateFolder), or its current name.</param>
    /// <param name="newName">The new folder name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The renamed stored folder (pass-through for chaining).</returns>
    [NodeName("Viewpoints.RenameFolder")]
    [NodeDescription("Renames a Saved Viewpoints folder (accepts the folder or its current name; searches nested folders too).")]
    [NodeSearchTags("viewpoints", "folder", "rename", "name", "organize")]
    [return: NodeName("folder")]
    public static FolderItem RenameFolder(object folder, string newName, Document? document = null)
    {
        if (string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("No new folder name provided.", nameof(newName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var stored = SavedItemTreeHelpers.ResolveStored<FolderItem>(viewpoints.RootItem, folder, "viewpoint folder");
        viewpoints.EditDisplayName(stored, newName);
        return stored;
    }

    /// <summary>Duplicates a saved viewpoint in place (same folder).</summary>
    /// <param name="viewpoint">The saved viewpoint, or its display name.</param>
    /// <param name="newName">Name for the copy (null uses "&lt;name&gt; copy").</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The new stored viewpoint copy.</returns>
    [NodeName("SavedViewpoint.Duplicate")]
    [NodeDescription("Duplicates a saved viewpoint in its folder, copying its camera and any baked appearance overrides. Names the copy \"<name> copy\" unless newName is given.")]
    [NodeSearchTags("viewpoint", "view", "duplicate", "copy", "clone")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Duplicate(object viewpoint, string? newName = null, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var stored = SavedItemTreeHelpers.ResolveStored<SavedViewpoint>(
            viewpoints.RootItem, viewpoint, "saved viewpoint");

        // Top-level items report RootItem as their parent; treat that as "no folder".
        var parentFolder = ReferenceEquals(stored.Parent, viewpoints.RootItem)
            ? null
            : stored.Parent as FolderItem;

        // AddCopy appends a copy at the end of the target collection.
        if (parentFolder != null)
        {
            viewpoints.AddCopy(parentFolder, stored);
        }
        else
        {
            viewpoints.AddCopy(stored);
        }

        var siblings = parentFolder != null ? parentFolder.Children : viewpoints.Value;
        var copy = (SavedViewpoint)siblings[siblings.Count - 1];
        var name = string.IsNullOrEmpty(newName) ? stored.DisplayName + " copy" : newName!;
        viewpoints.EditDisplayName(copy, name);
        return copy;
    }

    /// <summary>Duplicates a folder and all of its viewpoints (and sub-folders).</summary>
    /// <param name="folder">The source folder, or its name.</param>
    /// <param name="newName">Name for the new folder.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The new stored folder.</returns>
    [NodeName("Viewpoints.DuplicateFolder")]
    [NodeDescription("Duplicates a Saved Viewpoints folder — a new folder (created as a sibling) with copies of every viewpoint and nested sub-folder inside. An existing same-named target folder is reused, so re-runs top up rather than pile up.")]
    [NodeSearchTags("viewpoints", "folder", "duplicate", "copy", "clone", "organize")]
    [return: NodeName("folder")]
    public static FolderItem DuplicateFolder(object folder, string newName, Document? document = null)
    {
        if (string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("No new folder name provided.", nameof(newName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;
        var source = SavedItemTreeHelpers.ResolveStored<FolderItem>(viewpoints.RootItem, folder, "viewpoint folder");

        var sourceParent = ReferenceEquals(source.Parent, viewpoints.RootItem)
            ? null
            : source.Parent as FolderItem;
        var target = SavedItemTreeNodesShared.FindOrCreateFolder(
            viewpoints.RootItem,
            sourceParent,
            newName,
            item => viewpoints.AddCopy(item),
            (parent, item) => viewpoints.AddCopy(parent, item),
            "viewpoint");

        CopyFolderContents(viewpoints, source, target);
        return target;
    }

    /// <summary>Sorts a folder's contents alphabetically (A→Z) by name.</summary>
    /// <param name="folder">The folder to sort, or its name; null/empty sorts the top level.</param>
    /// <param name="recursive">True to also sort every nested folder.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The sorted folder (null when the top level was sorted).</returns>
    [NodeName("Viewpoints.SortFolder")]
    [NodeDescription("Sorts a Saved Viewpoints folder's contents alphabetically by name (A→Z) — so you never drag-and-drop views into order again. Pass no folder to sort the top level; set recursive to sort nested folders too. Folders sort before/among viewpoints by name.")]
    [NodeSearchTags("viewpoints", "folder", "sort", "alphabetical", "order", "organize", "arrange")]
    [return: NodeName("folder")]
    public static FolderItem? SortFolder(object? folder = null, bool recursive = false, Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;

        FolderItem parent = folder == null || (folder is string text && string.IsNullOrEmpty(text))
            ? viewpoints.RootItem
            : SavedItemTreeHelpers.ResolveStored<FolderItem>(viewpoints.RootItem, folder, "viewpoint folder");

        SortFolderContents(viewpoints, parent, recursive);
        return ReferenceEquals(parent, viewpoints.RootItem) ? null : parent;
    }

    // Recursively copies a source folder's children into a target folder: viewpoints
    // (and animations) are AddCopy'd; sub-folders are recreated and descended into.
    private static void CopyFolderContents(DocumentSavedViewpoints viewpoints, FolderItem source, FolderItem target)
    {
        foreach (var child in source.Children)
        {
            if (child is FolderItem subFolder)
            {
                var newSub = SavedItemTreeNodesShared.FindOrCreateFolder(
                    viewpoints.RootItem,
                    target,
                    subFolder.DisplayName,
                    item => viewpoints.AddCopy(item),
                    (parent, item) => viewpoints.AddCopy(parent, item),
                    "viewpoint");
                CopyFolderContents(viewpoints, subFolder, newSub);
            }
            else
            {
                viewpoints.AddCopy(target, child);
            }
        }
    }

    // Reorders one folder's children alphabetically by moving each item, in reverse
    // sorted order, to the front (index 0 — the one unambiguous Move target).
    private static void SortFolderContents(DocumentSavedViewpoints viewpoints, FolderItem parent, bool recursive)
    {
        var desired = new List<SavedItem>();
        foreach (var child in parent.Children)
        {
            desired.Add(child);
        }

        desired.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        for (int i = desired.Count - 1; i >= 0; i--)
        {
            int current = SavedItemTreeHelpers.IndexByIdentity(parent.Children, desired[i]);
            if (current > 0)
            {
                viewpoints.Move(parent, current, parent, 0);
            }
        }

        if (recursive)
        {
            foreach (var child in parent.Children)
            {
                if (child is FolderItem subFolder)
                {
                    SortFolderContents(viewpoints, subFolder, true);
                }
            }
        }
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
