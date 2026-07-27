using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// Groups axis-aligned boxes into connected clusters: two boxes belong
/// together when the gap between them is at most the tolerance, directly or
/// through a chain of other boxes (single-linkage connected components via
/// union-find). The geometry core of the Proximity.Cluster node — free of
/// Navisworks types so it is fully unit-testable.
/// </summary>
[IsVisibleInLibrary(false)]
public static class BoxClusterer
{
    /// <summary>
    /// Assigns a cluster id to every box. Ids are contiguous, starting at 0,
    /// numbered by each cluster's first appearance in the input order (so the
    /// result is deterministic for a given input). A null entry, or one that
    /// is not a 6-element [minX, minY, minZ, maxX, maxY, maxZ] array, gets -1.
    /// </summary>
    /// <param name="boxes">One box per element: [minX, minY, minZ, maxX, maxY, maxZ], or null for "no geometry".</param>
    /// <param name="tolerance">Maximum face-to-face gap (world units) that still counts as touching; 0 requires contact/overlap.</param>
    /// <returns>Cluster id per input index (-1 for boxless entries).</returns>
    public static int[] Cluster(IReadOnlyList<double[]?> boxes, double tolerance)
    {
        int count = boxes.Count;
        var parent = new int[count];
        var valid = new bool[count];
        for (int i = 0; i < count; i++)
        {
            parent[i] = i;
            var box = boxes[i];
            valid[i] = box != null && box.Length == 6 &&
                       box[0] <= box[3] && box[1] <= box[4] && box[2] <= box[5];
        }

        var tol = Math.Max(0, tolerance);

        // Sweep along X: after sorting by minX, box j can only touch box i
        // while boxes[j].minX <= boxes[i].maxX + tol — prunes the pair test
        // from all-pairs to near-neighbours.
        var order = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (valid[i])
            {
                order.Add(i);
            }
        }

        order.Sort((a, b) => boxes[a]![0].CompareTo(boxes[b]![0]));

        for (int si = 0; si < order.Count; si++)
        {
            var i = order[si];
            var a = boxes[i]!;
            for (int sj = si + 1; sj < order.Count; sj++)
            {
                var j = order[sj];
                var b = boxes[j]!;
                if (b[0] > a[3] + tol)
                {
                    break;
                }

                if (b[1] <= a[4] + tol && a[1] <= b[4] + tol &&
                    b[2] <= a[5] + tol && a[2] <= b[5] + tol)
                {
                    Union(parent, i, j);
                }
            }
        }

        // Relabel roots to contiguous ids in first-appearance order.
        var result = new int[count];
        var idOfRoot = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            if (!valid[i])
            {
                result[i] = -1;
                continue;
            }

            var root = Find(parent, i);
            if (!idOfRoot.TryGetValue(root, out var id))
            {
                id = idOfRoot.Count;
                idOfRoot[root] = id;
            }

            result[i] = id;
        }

        return result;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA != rootB)
        {
            parent[rootB] = rootA;
        }
    }
}
