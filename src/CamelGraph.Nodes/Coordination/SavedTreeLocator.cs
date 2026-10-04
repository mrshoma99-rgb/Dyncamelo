using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// Finds an item in a tree of folders and says which folder holds it and at which position, so an edit that needs the
/// parent (the Clash Detective tests tree: a test may sit in a folder) can address it. Pure (no Navisworks types), so it is
/// unit-tested; the Navisworks nodes pass lambdas that read their saved-item tree.
/// </summary>
[IsVisibleInLibrary(false)]
public static class SavedTreeLocator
{
    /// <summary>
    /// Looks for the first item that <paramref name="isTarget"/> accepts, in document order, descending into folders.
    /// </summary>
    /// <typeparam name="T">The node type of the tree.</typeparam>
    /// <param name="items">The top level of the tree.</param>
    /// <param name="isTarget">True for the item that is looked for.</param>
    /// <param name="folderContents">The children of a node when it is a folder to search inside, otherwise null (a leaf, or an item whose children are not part of the search).</param>
    /// <param name="parent">The folder that holds the item; null (default) when the item sits at the top level.</param>
    /// <param name="index">The item's position inside its folder (or the top level); -1 when not found.</param>
    /// <returns>True when the item was found.</returns>
    public static bool TryFind<T>(
        IEnumerable<T> items,
        Func<T, bool> isTarget,
        Func<T, IEnumerable<T>?> folderContents,
        out T? parent,
        out int index)
        where T : class
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (isTarget == null)
        {
            throw new ArgumentNullException(nameof(isTarget));
        }

        if (folderContents == null)
        {
            throw new ArgumentNullException(nameof(folderContents));
        }

        return Search(items, null, isTarget, folderContents, out parent, out index);
    }

    private static bool Search<T>(
        IEnumerable<T> items,
        T? owner,
        Func<T, bool> isTarget,
        Func<T, IEnumerable<T>?> folderContents,
        out T? parent,
        out int index)
        where T : class
    {
        var position = 0;
        foreach (var item in items)
        {
            if (isTarget(item))
            {
                parent = owner;
                index = position;
                return true;
            }

            var inside = folderContents(item);
            if (inside != null && Search(inside, item, isTarget, folderContents, out parent, out index))
            {
                return true;
            }

            position++;
        }

        parent = null;
        index = -1;
        return false;
    }
}
