using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.DocumentParts;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks;

/// <summary>
/// Folder management for the Sets window, mirroring the Saved Viewpoints folder nodes: read what a folder holds, sort it, rename it and
/// delete it. (Creating a folder is <c>SelectionSets.CreateFolder</c>; moving a set into one is <c>SelectionSet.MoveToFolder</c>.) All
/// edits go through <c>DocumentSelectionSets</c> part methods on the STORED items.
/// </summary>
[NodeCategory("Navisworks.SelectionSets")]
public static class SelectionSetFolderNodes
{
    /// <summary>Lists the selection and search sets inside a folder.</summary>
    /// <param name="folder">The folder to read: a folder object, its name, a path such as "Walls/Level 1", or empty for the top level of the Sets window.</param>
    /// <param name="recursive">True (default) includes sets in nested subfolders; false lists only the folder's direct children.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The sets in tree order, their names, the subfolders met on the way, and how many sets were found.</returns>
    [NodeName("SelectionSets.InFolder")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "All saved selection and search sets inside a folder, in Sets window order. Give it a folder object " +
        "(SelectionSets.CreateFolder / SelectionSet.Info), a folder NAME, or a path like \"Walls/Level 1\" " +
        "to pick between same-named folders; leave it empty for the top level. Nested subfolders are included " +
        "unless recursive is off. A list of folders gives one result per folder (List Levels are not needed). " +
        "Feeds straight into SelectionSet.Items, SelectionSet.Info or a Loop.Item. Re-reads the Sets window on every run.")]
    [NodeSearchTags("selection", "sets", "folder", "contents", "children", "list", "inside", "all")]
    [LiveState]
    [MultiReturn("selectionSets", "names", "subfolders", "count")]
    [PortKinds("selection*", "text*", "", "integer")]
    public static Dictionary<string, object?> InFolder(
        [ScalarInput][PortKinds("selection")] object? folder = null,
        bool recursive = true,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var start = SelectionSetFolders.Find(doc, folder, true, out _)
            ?? throw new InvalidOperationException(NoFolder(folder));

        var sets = new List<SelectionSet>();
        var subfolders = new List<FolderItem>();
        Collect(start.Children, recursive, sets, subfolders);

        var names = new List<string>(sets.Count);
        foreach (var set in sets)
        {
            names.Add(set.DisplayName ?? string.Empty);
        }

        return new Dictionary<string, object?>
        {
            ["selectionSets"] = sets,
            ["names"] = names,
            ["subfolders"] = subfolders,
            ["count"] = sets.Count,
        };
    }

    /// <summary>Sorts a folder's contents alphabetically (A to Z) by name.</summary>
    /// <param name="folder">The folder to sort: a folder object, its name or a path; empty sorts the top level.</param>
    /// <param name="recursive">True to also sort every nested folder.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The sorted folder (null when the top level was sorted).</returns>
    [NodeName("SelectionSets.SortFolder")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Sorts a Sets window folder's contents alphabetically by name (A to Z, ignoring case; entries with the same name keep their order) " +
        "so you never drag sets into order again. Give a folder object, a folder name or a path like \"Walls/Level 1\"; leave it empty to sort the " +
        "top level, and turn recursive on to sort the nested folders too. Folders are sorted among the sets by name. A folder that is " +
        "already in order is not touched, so re-runs are clean.")]
    [NodeSearchTags("selection", "sets", "folder", "sort", "alphabetical", "order", "organize", "arrange")]
    [return: NodeName("folder")]
    public static FolderItem? SortFolder(
        [ScalarInput][PortKinds("selection")] object? folder = null,
        bool recursive = false,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var parent = SelectionSetFolders.Find(doc, folder, true, out var isRoot)
            ?? throw new InvalidOperationException(NoFolder(folder));

        SortContents(doc.SelectionSets, parent, recursive);
        return isRoot ? null : parent as FolderItem;
    }

    /// <summary>Renames a Sets window folder.</summary>
    /// <param name="folder">The folder (e.g. from SelectionSets.CreateFolder), its current name, or a path such as "Walls/Level 1".</param>
    /// <param name="newName">The new folder name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The renamed stored folder (pass-through for chaining).</returns>
    [NodeName("SelectionSets.RenameFolder")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Renames a Sets window folder (accepts the folder, its current name or a path like \"Walls/Level 1\"; searches nested folders too). A list of folders with a list of new names renames them pair by pair.")]
    [NodeSearchTags("selection", "sets", "folder", "rename", "name", "organize")]
    [return: NodeName("folder")]
    public static FolderItem RenameFolder(
        [ScalarInput][PortKinds("selection")] object folder,
        string newName,
        Document? document = null)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("No new folder name provided.", nameof(newName));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var stored = SelectionSetFolders.Find(doc, folder, false, out _) as FolderItem
            ?? throw new InvalidOperationException(NoFolder(folder));
        doc.SelectionSets.EditDisplayName(stored, newName.Trim());
        return stored;
    }

    /// <summary>Deletes a Sets window folder.</summary>
    /// <param name="folder">The folder to delete: a folder object, its name or a path such as "Walls/Level 1".</param>
    /// <param name="deleteContents">False (default) refuses to delete a folder that still holds sets or folders; true deletes the folder together with everything inside it.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when a folder was deleted; false when there is no such folder.</returns>
    [NodeName("SelectionSets.DeleteFolder")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription(
        "Deletes a Sets window folder. A folder that still holds sets or folders is refused with a message unless deleteContents is on, " +
        "which deletes everything inside it as well — a run has no single undo, so check the folder first with SelectionSets.InFolder. " +
        "Returns false when there is no such folder, so a clean-up graph can run again safely.")]
    [NodeSearchTags("selection", "sets", "folder", "delete", "remove", "clean", "organize")]
    [return: NodeName("deleted")]
    public static bool DeleteFolder(
        [ScalarInput][PortKinds("selection")] object folder,
        bool deleteContents = false,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var stored = SelectionSetFolders.Find(doc, folder, false, out _) as FolderItem;
        if (stored == null)
        {
            return false;
        }

        if (stored.Children.Count > 0 && !deleteContents)
        {
            throw new InvalidOperationException(
                "The folder '" + stored.DisplayName + "' still holds " + stored.Children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " item(s). Move them out first, or turn on deleteContents to delete the folder together with everything inside it.");
        }

        var sets = doc.SelectionSets;
        var parent = stored.Parent;
        return parent == null ? sets.Remove(stored) : sets.Remove(parent, stored);
    }

    private static string NoFolder(object? folder)
    {
        if (folder is string text)
        {
            return "No sets folder '" + text + "' exists in the document.";
        }

        return folder is FolderItem item
            ? "The wired sets folder '" + item.DisplayName + "' is not stored in this document (was it deleted, or does it belong to another document?)."
            : "No such sets folder exists in the document.";
    }

    private static void Collect(IEnumerable<SavedItem> items, bool recursive, List<SelectionSet> sets, List<FolderItem> subfolders)
    {
        foreach (var item in items)
        {
            if (item is SelectionSet set)
            {
                sets.Add(set);
            }
            else if (item is FolderItem folder)
            {
                subfolders.Add(folder);
                if (recursive)
                {
                    Collect(folder.Children, true, sets, subfolders);
                }
            }
        }
    }

    // Reorders one folder's children alphabetically: the moves come from the pure SavedTreePaths rules (each entry to the front, in
    // reverse sorted order), and nothing is moved when the folder is already in order.
    private static void SortContents(DocumentSelectionSets sets, GroupItem parent, bool recursive)
    {
        var order = SavedTreePaths.SortedOrder(SelectionSetFolders.ChildNames(parent));
        foreach (var move in SavedTreePaths.MovesToFront(order))
        {
            sets.Move(parent, move.Key, parent, move.Value);
        }

        if (recursive)
        {
            foreach (var child in parent.Children)
            {
                if (child is FolderItem subFolder)
                {
                    SortContents(sets, subFolder, true);
                }
            }
        }
    }
}
