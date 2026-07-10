using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// Stored-item resolution for the saved-item trees (Sets window, Saved
/// Viewpoints window). All part edit methods (<c>EditDisplayName</c>,
/// <c>Move</c>, <c>AddComment</c>, <c>EditComments</c>, ...) require the STORED
/// item — <c>AddCopy</c> stores copies and stored items are <c>IsReadOnly</c>,
/// so nodes must never edit in place and must re-locate the stored instance for
/// whatever the user wired (a stored item, a stale copy, or a display name).
/// Internal — never surfaced as nodes.
/// </summary>
internal static class SavedItemTreeHelpers
{
    /// <summary>
    /// Resolves a node input to the STORED saved item of type
    /// <typeparamref name="T"/>: a string is looked up by display name; an item
    /// is located by reference, then by Guid, then by display name (same
    /// concrete type only). Throws a node-friendly error when nothing matches.
    /// </summary>
    /// <param name="root">The tree root (<c>part.RootItem</c>).</param>
    /// <param name="value">The node input (a <typeparamref name="T"/> or its display name).</param>
    /// <param name="kindLabel">Human label for error messages (e.g. "selection set").</param>
    internal static T ResolveStored<T>(FolderItem root, object? value, string kindLabel) where T : SavedItem
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No " + kindLabel + " provided.");
            case string name:
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("No " + kindLabel + " name provided.", nameof(value));
                }

                return FindByName<T>(root.Children, name)
                    ?? throw new InvalidOperationException(
                        "No " + kindLabel + " named '" + name + "' exists in the document.");
            case T item:
                return FindStoredEquivalent(root, item)
                    ?? throw new InvalidOperationException(
                        "The wired " + kindLabel + " '" + item.DisplayName +
                        "' is not stored in this document (was it deleted, or does it belong to another document?).");
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name + "' as a " + kindLabel +
                    ". Wire the " + kindLabel + " itself or its display name.", nameof(value));
        }
    }

    /// <summary>
    /// Locates the stored instance equivalent to <paramref name="target"/> in a
    /// tree: by reference first, then by Guid, then by display name (matching
    /// the exact concrete type so a same-named folder is never returned for a
    /// set, and vice versa). Null when the tree has no equivalent.
    /// </summary>
    internal static T? FindStoredEquivalent<T>(FolderItem root, T target) where T : SavedItem
    {
        var all = new List<T>();
        CollectAll(root.Children, all);

        foreach (var candidate in all)
        {
            if (ReferenceEquals(candidate, target))
            {
                return candidate;
            }
        }

        if (target.Guid != Guid.Empty)
        {
            foreach (var candidate in all)
            {
                if (candidate.Guid == target.Guid)
                {
                    return candidate;
                }
            }
        }

        var name = target.DisplayName;
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var candidate in all)
            {
                if (candidate.GetType() == target.GetType() &&
                    string.Equals(candidate.DisplayName, name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a tree contains the item (by reference or Guid only — no name
    /// fallback, so tree-ownership dispatch never guesses).
    /// </summary>
    internal static bool TreeContains(FolderItem root, SavedItem item)
    {
        var all = new List<SavedItem>();
        CollectAll(root.Children, all);

        foreach (var candidate in all)
        {
            if (ReferenceEquals(candidate, item))
            {
                return true;
            }
        }

        if (item.Guid != Guid.Empty)
        {
            foreach (var candidate in all)
            {
                if (candidate.Guid == item.Guid)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// First item of type <typeparamref name="T"/> with the given display name,
    /// searching the whole tree (folders descended, viewpoint animations treated
    /// as leaves). Unlike <c>NavisValues.FindSavedItemByName</c> this also
    /// descends into matched group items, so nested folders are found when
    /// <typeparamref name="T"/> is <see cref="FolderItem"/>.
    /// </summary>
    internal static T? FindByName<T>(IEnumerable<SavedItem> items, string displayName) where T : SavedItem
    {
        foreach (var item in items)
        {
            if (item is T match && string.Equals(match.DisplayName, displayName, StringComparison.Ordinal))
            {
                return match;
            }

            if (item is GroupItem group && !(item is SavedViewpointAnimation))
            {
                var found = FindByName<T>(group.Children, displayName);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The folder path of a stored item as "A/B" (top-level items give "") plus
    /// its immediate containing folder (null at the top level).
    /// </summary>
    internal static void GetFolderInfo(FolderItem root, SavedItem stored, out string folderPath, out FolderItem? folder)
    {
        folder = null;
        var segments = new List<string>();
        for (var parent = stored.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (folder == null && parent is FolderItem immediate)
            {
                folder = immediate;
            }

            if (!string.IsNullOrEmpty(parent.DisplayName))
            {
                segments.Insert(0, parent.DisplayName);
            }
        }

        folderPath = string.Join("/", segments);
    }

    /// <summary>
    /// Moves a stored item into a stored folder via the owning part's
    /// <c>Move(GroupItem, Int32, GroupItem, Int32)</c> (passed as
    /// <paramref name="move"/>). Already-in-place items are a no-op so re-runs
    /// are clean; the item is appended at the end of the folder.
    /// </summary>
    internal static T MoveToFolder<T>(
        FolderItem root,
        T stored,
        FolderItem storedFolder,
        Action<GroupItem, int, GroupItem, int> move,
        string kindLabel) where T : SavedItem
    {
        if (ReferenceEquals(stored, storedFolder))
        {
            throw new ArgumentException("Cannot move a folder into itself.", nameof(storedFolder));
        }

        var currentParent = (GroupItem?)stored.Parent ?? root;
        if (ReferenceEquals(currentParent, storedFolder) ||
            (storedFolder.Guid != Guid.Empty && currentParent.Guid == storedFolder.Guid))
        {
            return stored; // already in the target folder — no-op keeps re-runs clean
        }

        int oldIndex = currentParent.Children.IndexOf(stored);
        if (oldIndex < 0)
        {
            oldIndex = IndexByIdentity(currentParent.Children, stored);
        }

        if (oldIndex < 0)
        {
            throw new InvalidOperationException(
                "Could not locate the " + kindLabel + " '" + stored.DisplayName +
                "' under its parent — the tree changed while the node was running.");
        }

        move(currentParent, oldIndex, storedFolder, storedFolder.Children.Count);
        return stored;
    }

    /// <summary>Index of an item in a collection by reference/Guid identity, or -1.</summary>
    private static int IndexByIdentity(SavedItemCollection children, SavedItem item)
    {
        int index = 0;
        foreach (var child in children)
        {
            if (ReferenceEquals(child, item) || (item.Guid != Guid.Empty && child.Guid == item.Guid))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>
    /// Collects every <typeparamref name="T"/> in a saved-item tree, descending
    /// into ALL group items (including matches, so nested folders are seen) but
    /// treating saved viewpoint animations as leaves (their children are
    /// keyframes/cuts, not standalone saved items).
    /// </summary>
    private static void CollectAll<T>(IEnumerable<SavedItem> items, List<T> results) where T : SavedItem
    {
        foreach (var item in items)
        {
            if (item is T match)
            {
                results.Add(match);
            }

            if (item is GroupItem group && !(item is SavedViewpointAnimation))
            {
                CollectAll(group.Children, results);
            }
        }
    }
}
