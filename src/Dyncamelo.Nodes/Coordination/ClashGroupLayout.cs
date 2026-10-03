using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Coordination;

/// <summary>
/// How a test's results are laid out after a regrouping (Clash.GroupResultsByStatus, ...BySameItem,
/// ...ByProximity, ...ByLevel and the other nodes that rebuild the tree): the buckets of two or more become
/// named groups in bucket order, the single results follow loose. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
/// <typeparam name="T">What the buckets hold (the clash results).</typeparam>
public sealed class ClashGroupLayout<T>
{
    internal ClashGroupLayout(List<KeyValuePair<string, List<T>>> groups, List<T> singles)
    {
        Groups = groups;
        Singles = singles;
    }

    /// <summary>The groups to create, in order: a unique name and its two or more members.</summary>
    public IReadOnlyList<KeyValuePair<string, List<T>>> Groups { get; }

    /// <summary>The results that stay loose, in bucket order.</summary>
    public IReadOnlyList<T> Singles { get; }
}

/// <summary>Builds <see cref="ClashGroupLayout{T}"/>.</summary>
[IsVisibleInLibrary(false)]
public static class ClashGroupLayout
{
    /// <summary>
    /// Lays out the buckets: a bucket with fewer than two results stays loose; the others become groups named after
    /// the bucket, with " (2)", " (3)", ... added to a name that an earlier group already took.
    /// </summary>
    /// <typeparam name="T">What the buckets hold.</typeparam>
    /// <param name="buckets">The buckets in order: a name and the results in it.</param>
    /// <returns>The groups and the loose results.</returns>
    public static ClashGroupLayout<T> Build<T>(IEnumerable<KeyValuePair<string, List<T>>> buckets)
    {
        if (buckets == null)
        {
            throw new ArgumentNullException(nameof(buckets));
        }

        var groups = new List<KeyValuePair<string, List<T>>>();
        var singles = new List<T>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bucket in buckets)
        {
            if (bucket.Value.Count < 2)
            {
                singles.AddRange(bucket.Value);
                continue;
            }

            var name = bucket.Key;
            var suffix = 2;
            while (!usedNames.Add(name))
            {
                name = bucket.Key + " (" + suffix++ + ")";
            }

            groups.Add(new KeyValuePair<string, List<T>>(name, bucket.Value));
        }

        return new ClashGroupLayout<T>(groups, singles);
    }
}
