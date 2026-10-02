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
    /// <param name="verifyTouch">
    /// Optional exact-touch confirmation for a candidate pair (indices into
    /// <paramref name="boxes"/>): the box test is then only a prefilter, and a
    /// pair joins a cluster ONLY when this returns true. Never invoked for
    /// pairs already connected through earlier confirmations, so expensive
    /// checks (mesh clearance) run as few times as possible.
    /// </param>
    /// <returns>Cluster id per input index (-1 for boxless entries).</returns>
    public static int[] Cluster(
        IReadOnlyList<double[]?> boxes,
        double tolerance,
        Func<int, int, bool>? verifyTouch = null)
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

        // Sweep along one axis: after sorting by that axis' minimum, box j can only touch box i while
        // boxes[j].min <= boxes[i].max + tol — prunes the pair test from all-pairs to near-neighbours. The axis is the one the boxes
        // are spread over the most (X unless another is strictly longer): a model that is long in Y and narrow in X, swept along X,
        // would put thousands of boxes in the way of every box and test nearly all pairs (13 s for 198 000 boxes). Every touching pair
        // is still found whatever the axis, so the clusters are the same.
        var order = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (valid[i])
            {
                order.Add(i);
            }
        }

        var axis = WidestAxis(boxes, order);
        var p = (axis + 1) % 3;
        var q = (axis + 2) % 3;
        order.Sort((u, v) => boxes[u]![axis].CompareTo(boxes[v]![axis]));

        for (int si = 0; si < order.Count; si++)
        {
            var i = order[si];
            var a = boxes[i]!;
            for (int sj = si + 1; sj < order.Count; sj++)
            {
                var j = order[sj];
                var b = boxes[j]!;
                if (b[axis] > a[axis + 3] + tol)
                {
                    break;
                }

                if (b[p] <= a[p + 3] + tol && a[p] <= b[p + 3] + tol &&
                    b[q] <= a[q + 3] + tol && a[q] <= b[q + 3] + tol)
                {
                    if (verifyTouch == null)
                    {
                        Union(parent, i, j);
                    }
                    else if (Find(parent, i) != Find(parent, j) && verifyTouch(i, j))
                    {
                        Union(parent, i, j);
                    }
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

    /// <summary>0 (X), 1 (Y) or 2 (Z): the axis along which the valid boxes span the greatest distance; X when none is strictly greater.</summary>
    private static int WidestAxis(IReadOnlyList<double[]?> boxes, List<int> valid)
    {
        if (valid.Count < 2)
        {
            return 0;
        }

        var low = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var high = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        foreach (var index in valid)
        {
            var box = boxes[index]!;
            for (int d = 0; d < 3; d++)
            {
                if (box[d] < low[d])
                {
                    low[d] = box[d];
                }

                if (box[d + 3] > high[d])
                {
                    high[d] = box[d + 3];
                }
            }
        }

        var best = 0;
        var bestSpan = high[0] - low[0];
        for (int d = 1; d < 3; d++)
        {
            var span = high[d] - low[d];
            if (span > bestSpan)
            {
                best = d;
                bestSpan = span;
            }
        }

        return best;
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
