using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// The pure half of Selection.Remove: taking a list of requested items out of a list in one pass. Removing them one at a time from
/// a Navisworks collection searched the whole collection for every item (20 000 items out of a selection of 100 000 was 20 000
/// searches through up to 100 000 entries); a hash of the requests makes it one walk. Pure (no Navisworks types), so it is
/// unit-tested; Selection.Remove supplies the comparer that says when two wrappers are the same model item.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ListRemoval
{
    /// <summary>
    /// Removes each requested item from the list once, the way calling <c>Remove</c> for every request in turn would: a request takes
    /// out the FIRST remaining entry equal to it, a request with no entry left is ignored, and an item that is requested twice but
    /// listed once is removed once. The order of the entries that stay is the list's order.
    /// </summary>
    /// <typeparam name="T">The item type (never null in either list).</typeparam>
    /// <param name="source">The list to take items out of (not changed).</param>
    /// <param name="requests">The items to take out; null entries are ignored.</param>
    /// <param name="comparer">Says when two entries are the same item (its hash code must agree with it).</param>
    /// <param name="removedCount">How many entries were taken out.</param>
    /// <returns>The entries that stay, in order.</returns>
    public static List<T> RemoveFirstOfEach<T>(
        IReadOnlyList<T> source,
        IEnumerable<T> requests,
        IEqualityComparer<T> comparer,
        out int removedCount)
        where T : class
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (requests == null)
        {
            throw new ArgumentNullException(nameof(requests));
        }

        if (comparer == null)
        {
            throw new ArgumentNullException(nameof(comparer));
        }

        var pending = new Dictionary<T, int>(comparer);
        foreach (var request in requests)
        {
            if (request == null)
            {
                continue;
            }

            pending.TryGetValue(request, out var count);
            pending[request] = count + 1;
        }

        removedCount = 0;
        var remaining = new List<T>(source.Count);
        if (pending.Count == 0)
        {
            remaining.AddRange(source);
            return remaining;
        }

        foreach (var entry in source)
        {
            if (entry == null)
            {
                remaining.Add(entry!);
            }
            else if (pending.TryGetValue(entry, out var count) && count > 0)
            {
                pending[entry] = count - 1;
                removedCount++;
            }
            else
            {
                remaining.Add(entry);
            }
        }

        return remaining;
    }
}
