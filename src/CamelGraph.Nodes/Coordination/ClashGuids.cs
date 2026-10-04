using System;
using System.Collections.Generic;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Coordination;

/// <summary>
/// Finds clash results (or result groups) again from the GUIDs that BCF topics and snapshot files carry (ClashResult.ByGuid).
/// Pure (no Navisworks types), so it is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ClashGuids
{
    /// <summary>Reads a GUID from text: surrounding spaces, braces and any letter case are accepted.</summary>
    /// <param name="text">The text.</param>
    /// <param name="guid">The GUID read.</param>
    /// <returns>True when the text is a GUID other than the all-zero one.</returns>
    public static bool TryParse(string? text, out Guid guid)
    {
        guid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Guid.TryParse(text!.Trim(), out guid) && guid != Guid.Empty;
    }

    /// <summary>
    /// Matches wanted GUIDs (as text) against candidates. Found things come out in the order of the wanted list, one per wanted
    /// GUID that exists (a GUID asked for twice is found twice); everything that is not a GUID or not among the candidates comes out
    /// in <paramref name="missing"/> as it was written.
    /// </summary>
    /// <typeparam name="T">What the candidates are (clash results and groups).</typeparam>
    /// <param name="wanted">The GUIDs asked for, as text.</param>
    /// <param name="candidates">Every candidate with its GUID.</param>
    /// <param name="found">The candidates that were asked for.</param>
    /// <param name="missing">The wanted entries that matched nothing.</param>
    public static void Match<T>(
        IEnumerable<string?> wanted,
        IEnumerable<KeyValuePair<Guid, T>> candidates,
        out List<T> found,
        out List<string> missing)
        where T : class
    {
        if (wanted == null)
        {
            throw new ArgumentNullException(nameof(wanted));
        }

        if (candidates == null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var byGuid = new Dictionary<Guid, T>();
        foreach (var candidate in candidates)
        {
            if (candidate.Key != Guid.Empty && !byGuid.ContainsKey(candidate.Key))
            {
                byGuid[candidate.Key] = candidate.Value;
            }
        }

        found = new List<T>();
        missing = new List<string>();
        foreach (var text in wanted)
        {
            if (TryParse(text, out var guid) && byGuid.TryGetValue(guid, out var item))
            {
                found.Add(item);
            }
            else
            {
                missing.Add(text ?? string.Empty);
            }
        }
    }
}
