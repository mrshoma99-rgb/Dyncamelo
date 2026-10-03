using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// The pure half of BCF.ImportIssues' fallback search by IFC GlobalId. Looking each GUID up with a whole-model search of its own
/// walks the model once per GUID; one search with an OR group per GUID walks it once, but then says only which items matched
/// SOME GUID, so the items have to be given back to the GUIDs they matched. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class GlobalIdMatching
{
    /// <summary>
    /// How many GUIDs one search carries as OR groups. A package with more unmatched GUIDs than this is searched in batches, so a
    /// search never grows without bound; the usual package has a few dozen.
    /// </summary>
    public const int MaxGuidsPerSearch = 500;

    /// <summary>Splits the GUIDs into batches of at most <paramref name="batchSize"/>, keeping their order.</summary>
    /// <param name="guids">The GUIDs to look up.</param>
    /// <param name="batchSize">The most GUIDs in one batch (at least 1).</param>
    /// <returns>The batches (none for no GUIDs).</returns>
    public static List<List<string>> Batch(IReadOnlyList<string> guids, int batchSize = MaxGuidsPerSearch)
    {
        if (guids == null)
        {
            throw new ArgumentNullException(nameof(guids));
        }

        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "A batch holds at least one GUID.");
        }

        var batches = new List<List<string>>();
        for (var start = 0; start < guids.Count; start += batchSize)
        {
            var batch = new List<string>(Math.Min(batchSize, guids.Count - start));
            for (var i = start; i < guids.Count && i < start + batchSize; i++)
            {
                batch.Add(guids[i]);
            }

            batches.Add(batch);
        }

        return batches;
    }

    /// <summary>
    /// Gives the items of a combined search back to the GUIDs they matched: each item goes to the GUID its own GlobalId text equals
    /// (exact, case-sensitive text, the way the search compares), in the order the items were found, which is the order a search
    /// for that GUID alone would give. Only when EVERY item can be given to a GUID is the result usable: an item whose text matches
    /// none of the GUIDs means the search matched by a rule this method does not know, so nothing is returned and the caller looks
    /// the GUIDs up one at a time instead.
    /// </summary>
    /// <typeparam name="TItem">The item type (a model item).</typeparam>
    /// <param name="found">The items the combined search found, in model order.</param>
    /// <param name="globalIdOf">The GlobalId text of an item, or null when it has none that can be read.</param>
    /// <param name="guids">The GUIDs the combined search was made for.</param>
    /// <param name="itemsByGuid">The items of every GUID that has any (a GUID without items is not listed).</param>
    /// <returns>True when every found item was given to a GUID.</returns>
    public static bool TryMapToGuids<TItem>(
        IEnumerable<TItem> found,
        Func<TItem, string?> globalIdOf,
        IEnumerable<string> guids,
        out Dictionary<string, List<TItem>> itemsByGuid)
    {
        if (found == null)
        {
            throw new ArgumentNullException(nameof(found));
        }

        if (globalIdOf == null)
        {
            throw new ArgumentNullException(nameof(globalIdOf));
        }

        if (guids == null)
        {
            throw new ArgumentNullException(nameof(guids));
        }

        var wanted = new HashSet<string>(guids, StringComparer.Ordinal);
        itemsByGuid = new Dictionary<string, List<TItem>>(StringComparer.Ordinal);
        foreach (var item in found)
        {
            var text = globalIdOf(item);
            if (text == null || !wanted.Contains(text))
            {
                itemsByGuid = new Dictionary<string, List<TItem>>(StringComparer.Ordinal);
                return false;
            }

            if (!itemsByGuid.TryGetValue(text, out var items))
            {
                items = new List<TItem>();
                itemsByGuid[text] = items;
            }

            items.Add(item);
        }

        return true;
    }
}
