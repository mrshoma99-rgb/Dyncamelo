using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// Says which of the live clash results were already in a saved snapshot (Clash.FilterBySnapshot), by the same identity keys
/// Clash.CompareSnapshots uses, so "new" and "persisting" mean the same in both nodes. Pure (no Navisworks types), so it is
/// unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashSnapshotMatch
{
    /// <summary>
    /// For every live result: true when the snapshot already had it (persisting), false when it is new. A key that appears more
    /// than once (the same two items clashing at several points) is matched one for one, in order: if the snapshot had two of
    /// them and there are three live, the first two persist and the third is new.
    /// </summary>
    /// <param name="snapshotKeys">The identity key of every result in the snapshot.</param>
    /// <param name="liveKeys">The identity key of every live result, in input order.</param>
    /// <returns>One flag per live result: true = was in the snapshot.</returns>
    public static bool[] WasInSnapshot(IEnumerable<string> snapshotKeys, IReadOnlyList<string> liveKeys)
    {
        if (snapshotKeys == null)
        {
            throw new ArgumentNullException(nameof(snapshotKeys));
        }

        if (liveKeys == null)
        {
            throw new ArgumentNullException(nameof(liveKeys));
        }

        var available = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in snapshotKeys)
        {
            var name = key ?? string.Empty;
            available.TryGetValue(name, out var count);
            available[name] = count + 1;
        }

        var flags = new bool[liveKeys.Count];
        for (var i = 0; i < liveKeys.Count; i++)
        {
            var key = liveKeys[i] ?? string.Empty;
            if (available.TryGetValue(key, out var left) && left > 0)
            {
                available[key] = left - 1;
                flags[i] = true;
            }
        }

        return flags;
    }
}
