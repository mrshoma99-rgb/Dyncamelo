using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

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
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Renames a saved viewpoint (accepts the viewpoint, its current name or a folder path and name such as \"Reviews/Week 12/Level 1\"; searches folders too). Batch-rename by wiring a list of viewpoints and a list of new names: each viewpoint gets the new name at the same position.")]
    [NodeSearchTags("viewpoint", "view", "rename", "name", "batch")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Rename([ScalarInput] object viewpoint, string newName, Document? document = null)
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
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
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
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Moves a saved viewpoint into a folder (appended at the end). A viewpoint already in the folder is left alone, so re-runs are clean. A list of viewpoints moves each one into the folder; a list of folders pairs with the viewpoints one to one.")]
    [NodeSearchTags("viewpoint", "view", "move", "folder", "organize")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint MoveToFolder([ScalarInput] object viewpoint, [ScalarInput] object folder, Document? document = null)
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
    [LiveState]
    [NodeDescription("The folder containing a saved viewpoint: its path as \"A/B\" (\"\" for top-level viewpoints) and the folder itself — drives folder-based status workflows. A list of viewpoints gives one path and one folder per viewpoint. Read again on every run.")]
    [NodeSearchTags("viewpoint", "view", "folder", "path", "parent", "location")]
    [MultiReturn("folderPath", "folder")]
    [PortKinds("text", "")]
    public static Dictionary<string, object?> Folder([ScalarInput] object viewpoint, Document? document = null)
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

    /// <summary>Lists the saved viewpoints inside a folder.</summary>
    /// <param name="folder">The folder to read: a folder object, its name, a path such as "Reviews/Week 12", or empty for the top level of the Saved Viewpoints window.</param>
    /// <param name="recursive">True (default) includes viewpoints in nested subfolders; false lists only the folder's direct children.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The viewpoints in tree order, their names, the subfolders met on the way, and how many viewpoints were found.</returns>
    [NodeName("Viewpoints.InFolder")]
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [LiveState]
    [NodeDescription(
        "All saved viewpoints inside a folder, in Saved Viewpoints window order. Give it a folder object " +
        "(Viewpoints.CreateFolder / SavedViewpoint.Folder), a folder NAME, or a path like \"Reviews/Week 12\" " +
        "to pick between same-named folders; leave it empty for the top level. Nested subfolders are included " +
        "unless recursive is off. A list of folders gives one list of viewpoints per folder. Read again on every run. " +
        "Feeds straight into SavedViewpoint.Apply, Export.ViewpointImage (wire the viewpoints to its own viewpoint input " +
        "for a picture of each), Viewpoints.ExportFile or a Loop.Item.")]
    [NodeSearchTags("viewpoints", "folder", "contents", "children", "list", "inside", "views", "all")]
    [MultiReturn("viewpoints", "names", "subfolders", "count")]
    [PortKinds("viewpoint*", "text*", "", "integer")]
    public static Dictionary<string, object?> InFolder(
        [ScalarInput] object? folder = null,
        bool recursive = true,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var root = doc.SavedViewpoints.RootItem;
        var start = ResolveFolderOrRoot(root, folder);

        var viewpoints = new List<SavedViewpoint>();
        var subfolders = new List<FolderItem>();
        CollectViewpoints(start.Children, recursive, viewpoints, subfolders);

        var names = new List<string>(viewpoints.Count);
        foreach (var viewpoint in viewpoints)
        {
            names.Add(viewpoint.DisplayName ?? string.Empty);
        }

        return new Dictionary<string, object?>
        {
            ["viewpoints"] = viewpoints,
            ["names"] = names,
            ["subfolders"] = subfolders,
            ["count"] = viewpoints.Count,
        };
    }

    /// <summary>
    /// Resolves the folder input: null/empty = the tree root, a folder object,
    /// a "A/B" path (each segment matched among the previous folder's direct
    /// children), or a bare name (first match anywhere in the tree).
    /// </summary>
    private static GroupItem ResolveFolderOrRoot(FolderItem root, object? folder)
    {
        switch (folder)
        {
            case null:
                return root;
            case string text when text.Trim().Length == 0:
                return root;
            case string path when path.IndexOf('/') >= 0:
                GroupItem current = root;
                foreach (var segment in path.Split('/'))
                {
                    var name = segment.Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    GroupItem? next = null;
                    foreach (var child in current.Children)
                    {
                        if (child is FolderItem candidate &&
                            string.Equals(candidate.DisplayName, name, StringComparison.Ordinal))
                        {
                            next = candidate;
                            break;
                        }
                    }

                    current = next ?? throw new InvalidOperationException(
                        "No viewpoint folder '" + name + "' exists in the path '" + path + "'.");
                }

                return current;
            default:
                return SavedItemTreeHelpers.ResolveStored<FolderItem>(root, folder, "viewpoint folder");
        }
    }

    private static void CollectViewpoints(
        IEnumerable<SavedItem> items,
        bool recursive,
        List<SavedViewpoint> viewpoints,
        List<FolderItem> subfolders)
    {
        foreach (var item in items)
        {
            if (item is SavedViewpoint viewpoint)
            {
                viewpoints.Add(viewpoint);
            }
            else if (item is FolderItem folder)
            {
                subfolders.Add(folder);
                if (recursive)
                {
                    CollectViewpoints(folder.Children, true, viewpoints, subfolders);
                }
            }
        }
    }

    /// <summary>Renames a saved-viewpoint folder.</summary>
    /// <param name="folder">The folder (e.g. from Viewpoints.CreateFolder), or its current name.</param>
    /// <param name="newName">The new folder name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The renamed stored folder (pass-through for chaining).</returns>
    [NodeName("Viewpoints.RenameFolder")]
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Renames a Saved Viewpoints folder (accepts the folder, its current name or a path such as \"Reviews/Week 12\"; searches nested folders too). A list of folders and a list of new names rename one folder per name.")]
    [NodeSearchTags("viewpoints", "folder", "rename", "name", "organize")]
    [return: NodeName("folder")]
    public static FolderItem RenameFolder([ScalarInput] object folder, string newName, Document? document = null)
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
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Duplicates a saved viewpoint in its folder, copying its camera and any baked appearance overrides. Names the copy \"<name> copy\" unless newName is given. A list of viewpoints makes one copy of each.")]
    [NodeSearchTags("viewpoint", "view", "duplicate", "copy", "clone")]
    [return: NodeName("viewpoint")]
    public static SavedViewpoint Duplicate([ScalarInput] object viewpoint, string? newName = null, Document? document = null)
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
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Duplicates a Saved Viewpoints folder — a new folder (created as a sibling) with copies of every viewpoint and nested sub-folder inside. An existing same-named target folder is reused and topped up: a viewpoint that is already in it under the same name is replaced by the copy, a missing one is added, so re-runs do not pile up. The new name must differ from the source folder's own name. A list of folders and a list of new names duplicate one folder per name.")]
    [NodeSearchTags("viewpoints", "folder", "duplicate", "copy", "clone", "organize")]
    [return: NodeName("folder")]
    public static FolderItem DuplicateFolder([ScalarInput] object folder, string newName, Document? document = null)
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

        // A new name equal to the source's own name finds the source itself, and copying a folder into itself would add to the
        // list it is reading from: refuse before anything is copied.
        if (ReferenceEquals(target, source) || (source.Guid != Guid.Empty && target.Guid == source.Guid))
        {
            throw new InvalidOperationException(
                "The new name '" + newName + "' is the name of the folder being duplicated. Give the copy a different name.");
        }

        CopyFolderContents(viewpoints, source, target);
        return target;
    }

    /// <summary>Deletes a folder of saved viewpoints, or just empties it.</summary>
    /// <param name="folder">The folder (for example from Viewpoints.CreateFolder), its name, or a path such as "Reviews/Week 12".</param>
    /// <param name="contentsOnly">True removes everything inside the folder and keeps the folder; false removes the folder with everything in it.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Whether anything was deleted, and how many saved viewpoints were inside (nested folders included).</returns>
    [NodeName("Viewpoints.DeleteFolder")]
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Deletes a Saved Viewpoints folder together with every viewpoint and sub-folder in it, or with contentsOnly on empties the folder and keeps it — the clean-up before Viewpoints.FromClashResults fills \"Clash Views\" again. Accepts the folder, its name or a path such as \"Reviews/Week 12\". Returns deleted = false when there is no such folder (or it is already empty), so a clean-up step can run twice. A list of folders deletes one folder per entry. This cannot be undone from the graph.")]
    [NodeSearchTags("viewpoints", "folder", "delete", "remove", "clean", "empty", "clear", "organize")]
    [MultiReturn("deleted", "viewpointCount")]
    [PortKinds("boolean", "integer")]
    public static Dictionary<string, object?> DeleteFolder(
        [ScalarInput] object folder,
        bool contentsOnly = false,
        Document? document = null)
    {
        if (folder == null)
        {
            throw new ArgumentNullException(nameof(folder), "No viewpoint folder provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;

        FolderItem? stored;
        switch (folder)
        {
            case string text:
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("No folder name provided.", nameof(folder));
                }

                stored = SavedItemTreeHelpers.FindByNameOrPath<FolderItem>(viewpoints.RootItem, text, "viewpoint folder");
                break;
            case FolderItem item:
                try
                {
                    stored = SavedItemTreeHelpers.FindStoredEquivalent(viewpoints.RootItem, item);
                }
                catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
                {
                    NodeWarnings.Add("A folder wired to Viewpoints.DeleteFolder was already removed or replaced by an earlier edit, so nothing was deleted.");
                    stored = null;
                }

                break;
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + folder.GetType().Name +
                    "' as a viewpoint folder. Wire the folder itself, its name or its path.", nameof(folder));
        }

        if (stored == null)
        {
            return new Dictionary<string, object?> { ["deleted"] = false, ["viewpointCount"] = 0 };
        }

        var count = NavisValues.FlattenSavedItems<SavedViewpoint>(stored.Children).Count;
        if (contentsOnly)
        {
            var childCount = stored.Children.Count;
            for (int i = childCount - 1; i >= 0; i--)
            {
                viewpoints.RemoveAt(stored, i);
            }

            return new Dictionary<string, object?> { ["deleted"] = childCount > 0, ["viewpointCount"] = count };
        }

        var parent = stored.Parent;
        var removed = parent == null ? viewpoints.Remove(stored) : viewpoints.Remove(parent, stored);
        return new Dictionary<string, object?> { ["deleted"] = removed, ["viewpointCount"] = count };
    }

    /// <summary>Sorts a folder's contents alphabetically (A→Z) by name.</summary>
    /// <param name="folder">The folder to sort, or its name; null/empty sorts the top level.</param>
    /// <param name="recursive">True to also sort every nested folder.</param>
    /// <param name="numeric">True sorts runs of digits by their number (Clash2 before Clash10); false sorts letter by letter (Clash10 before Clash2).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The sorted folder (null when the top level was sorted).</returns>
    [NodeName("Viewpoints.SortFolder")]
    [NodeCategory("Navisworks.Viewpoints.Folders")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ViewpointTreeNodes.SortFolder@object,bool,Autodesk.Navisworks.Api.Document")]
    [NodeDescription("Sorts a Saved Viewpoints folder's contents alphabetically by name (A→Z), ignoring case — so you never drag-and-drop views into order again. Pass no folder to sort the top level; set recursive to sort nested folders too. Folders sort among the viewpoints by name. Turn numeric on to put Clash2 before Clash10 (letter by letter, \"Clash10\" comes first).")]
    [NodeSearchTags("viewpoints", "folder", "sort", "alphabetical", "order", "organize", "arrange", "numeric", "natural")]
    [return: NodeName("folder")]
    public static FolderItem? SortFolder(
        [ScalarInput] object? folder = null,
        bool recursive = false,
        bool numeric = false,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoints = doc.SavedViewpoints;

        FolderItem parent = folder == null || (folder is string text && string.IsNullOrEmpty(text))
            ? viewpoints.RootItem
            : SavedItemTreeHelpers.ResolveStored<FolderItem>(viewpoints.RootItem, folder, "viewpoint folder");

        IComparer<string?> comparer = numeric
            ? NaturalNameComparer.Instance
            : StringComparer.OrdinalIgnoreCase;
        SortFolderContents(viewpoints, parent, recursive, comparer);
        return ReferenceEquals(parent, viewpoints.RootItem) ? null : parent;
    }

    // Recursively copies a source folder's children into a target folder. Sub-folders are recreated and descended into. A
    // viewpoint (or animation) whose name is already in the target is REPLACED by the copy, anything else is added, so running the
    // node again tops the target up instead of doubling it.
    private static void CopyFolderContents(DocumentSavedViewpoints viewpoints, FolderItem source, FolderItem target)
    {
        // What the target already holds, by name and kind, read once; the source's own list is copied first so adding to the
        // target can never change the list being walked.
        var existing = new Dictionary<string, int>(StringComparer.Ordinal);
        var position = 0;
        foreach (var child in target.Children)
        {
            if (!(child is FolderItem) && !existing.ContainsKey(child.DisplayName))
            {
                existing[child.DisplayName] = position;
            }

            position++;
        }

        var items = new List<SavedItem>();
        foreach (var child in source.Children)
        {
            items.Add(child);
        }

        foreach (var child in items)
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
            else if (existing.TryGetValue(child.DisplayName, out var index) &&
                     index < target.Children.Count &&
                     target.Children[index].GetType() == child.GetType())
            {
                viewpoints.ReplaceWithCopy(target, index, child);
            }
            else
            {
                viewpoints.AddCopy(target, child);
                existing[child.DisplayName] = target.Children.Count - 1;
            }
        }
    }

    // Reorders one folder's children by name by moving each item, in reverse
    // sorted order, to the front (index 0 — the one unambiguous Move target).
    private static void SortFolderContents(
        DocumentSavedViewpoints viewpoints, FolderItem parent, bool recursive, IComparer<string?> comparer)
    {
        var desired = new List<SavedItem>();
        foreach (var child in parent.Children)
        {
            desired.Add(child);
        }

        desired.Sort((a, b) => comparer.Compare(a.DisplayName, b.DisplayName));

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
                    SortFolderContents(viewpoints, subFolder, true, comparer);
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
