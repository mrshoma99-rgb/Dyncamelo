using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using CamelGraph.Nodes.Portable;

namespace CamelGraph.Navisworks.Internal;

/// <summary>
/// Finds and creates the folders of the Sets window for the nodes that take a <c>folder</c> input: a stored folder, a folder name, or a
/// path such as "Walls/Level 1". The reading rules (how a path is split, in which order a sort puts entries) are the pure helpers of
/// <see cref="SavedTreePaths"/>. Internal — never surfaced as nodes.
/// </summary>
internal static class SelectionSetFolders
{
    /// <summary>
    /// The folder a creator node files its set in. Null or blank means the top level (the method returns null). A folder object is
    /// located among the stored folders. A name finds the first folder of that name anywhere in the tree, or creates one at the top
    /// level; a path "A/B" is followed from the top level, creating every folder that does not exist yet.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="folder">What the node's folder input holds.</param>
    internal static FolderItem? ResolveOrCreate(Document doc, object? folder)
    {
        var sets = doc.SelectionSets;
        switch (folder)
        {
            case null:
                return null;
            case string text:
                var names = SavedTreePaths.Split(text);
                if (names.Count == 0)
                {
                    return null;
                }

                if (names.Count == 1)
                {
                    var existing = SavedItemTreeHelpers.FindByName<FolderItem>(sets.RootItem.Children, names[0]);
                    if (existing != null)
                    {
                        return existing;
                    }
                }

                FolderItem? current = null;
                foreach (var name in names)
                {
                    current = SavedItemTreeNodesShared.FindOrCreateFolder(
                        sets.RootItem,
                        current,
                        name,
                        item => sets.AddCopy(item),
                        (parent, item) => sets.AddCopy(parent, item),
                        "sets");
                }

                return current;
            case FolderItem item:
                return SavedItemTreeHelpers.ResolveStored<FolderItem>(sets.RootItem, item, "sets folder");
            default:
                throw new ArgumentException(
                    "The folder input takes a folder (from SelectionSets.CreateFolder), a folder name or a path such as \"Walls/Level 1\". Wire one of those.",
                    nameof(folder));
        }
    }

    /// <summary>
    /// The existing folder an input names, without creating anything: the tree root for null or blank (when
    /// <paramref name="allowRoot"/>), a folder object, a "A/B" path followed from the top level, or a bare name (the first folder of that
    /// name anywhere). Null when there is no such folder.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="folder">What the node's folder input holds.</param>
    /// <param name="allowRoot">True when an empty input means the top level of the Sets window.</param>
    /// <param name="isRoot">True when the result is the top level.</param>
    internal static GroupItem? Find(Document doc, object? folder, bool allowRoot, out bool isRoot)
    {
        var root = doc.SelectionSets.RootItem;
        isRoot = false;
        switch (folder)
        {
            case null:
                isRoot = allowRoot;
                return allowRoot ? root : null;
            case string text:
                var names = SavedTreePaths.Split(text);
                if (names.Count == 0)
                {
                    isRoot = allowRoot;
                    return allowRoot ? root : null;
                }

                if (names.Count == 1)
                {
                    return SavedItemTreeHelpers.FindByName<FolderItem>(root.Children, names[0]);
                }

                GroupItem current = root;
                foreach (var name in names)
                {
                    FolderItem? next = null;
                    foreach (var child in current.Children)
                    {
                        if (child is FolderItem candidate && string.Equals(candidate.DisplayName, name, StringComparison.Ordinal))
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
                }

                return current;
            case FolderItem item:
                return SavedItemTreeHelpers.FindStoredEquivalent(root, item);
            default:
                throw new ArgumentException(
                    "The folder input takes a folder (from SelectionSets.CreateFolder), a folder name or a path such as \"Walls/Level 1\". Wire one of those.",
                    nameof(folder));
        }
    }

    /// <summary>The names of a folder's direct children, in tree order (null for an entry without a name).</summary>
    /// <param name="parent">The folder or the root.</param>
    internal static List<string?> ChildNames(GroupItem parent)
    {
        var names = new List<string?>();
        foreach (var child in parent.Children)
        {
            names.Add(child.DisplayName);
        }

        return names;
    }
}
