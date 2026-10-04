using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The pure half of the Transform nodes' "do not move things twice" rule. A transform applied to a container (a group, a layer, a
/// file) already moves everything beneath it, so a list that holds a container AND items below it would move those items twice.
/// This keeps only the items that have no listed item above them, and each item once. Pure (no Navisworks types), so it is
/// unit-tested; the Navisworks nodes supply the ancestor walk and the comparer that says when two wrappers are the same item.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ListedAncestors
{
    /// <summary>
    /// Leaves out every item that has another item of the same list above it in the tree, and every repeat of an item already kept.
    /// The order of the items that stay is the list's order.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items (not changed).</param>
    /// <param name="ancestorsOf">The ancestors of one item, nearest first (the item itself not included).</param>
    /// <param name="comparer">Says when two entries are the same item (its hash code must agree with it).</param>
    /// <param name="dropped">How many entries were left out.</param>
    /// <returns>The entries that stay.</returns>
    public static List<T> DropDescendantsAndRepeats<T>(
        IReadOnlyList<T> items,
        Func<T, IEnumerable<T>> ancestorsOf,
        IEqualityComparer<T> comparer,
        out int dropped)
        where T : class
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (ancestorsOf == null)
        {
            throw new ArgumentNullException(nameof(ancestorsOf));
        }

        if (comparer == null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        var listed = new HashSet<T>(comparer);
        foreach (var item in items)
        {
            listed.Add(item);
        }

        var kept = new List<T>(items.Count);
        var seen = new HashSet<T>(comparer);
        foreach (var item in items)
        {
            var below = false;
            foreach (var ancestor in ancestorsOf(item))
            {
                if (listed.Contains(ancestor))
                {
                    below = true;
                    break;
                }
            }

            if (!below && seen.Add(item))
            {
                kept.Add(item);
            }
        }

        dropped = items.Count - kept.Count;
        return kept;
    }
}
