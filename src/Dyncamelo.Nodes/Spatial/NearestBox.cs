using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// An axis-aligned box as six plain numbers, so the geometry of Proximity.NearestDistance can be worked out without calling the
/// Navisworks API for every number of every pair (a box was read with about a dozen API calls per pair of boxes). Free of
/// Navisworks types, so it is fully unit-testable.
/// </summary>
public readonly struct AxisBox
{
    /// <summary>Creates a box.</summary>
    /// <param name="minX">Smallest X.</param>
    /// <param name="minY">Smallest Y.</param>
    /// <param name="minZ">Smallest Z.</param>
    /// <param name="maxX">Largest X.</param>
    /// <param name="maxY">Largest Y.</param>
    /// <param name="maxZ">Largest Z.</param>
    public AxisBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    /// <summary>Smallest X.</summary>
    public double MinX { get; }

    /// <summary>Smallest Y.</summary>
    public double MinY { get; }

    /// <summary>Smallest Z.</summary>
    public double MinZ { get; }

    /// <summary>Largest X.</summary>
    public double MaxX { get; }

    /// <summary>Largest Y.</summary>
    public double MaxY { get; }

    /// <summary>Largest Z.</summary>
    public double MaxZ { get; }
}

/// <summary>The distance between two boxes, worked out exactly as Proximity.NearestDistance always has.</summary>
[IsVisibleInLibrary(false)]
public static class BoxGeometry
{
    // Coordinates beyond this (and NaN) are not ordinary: the pruning of NearestBoxIndex only reasons about boxes whose numbers
    // are finite, ordered and small enough that adding two of them cannot overflow.
    private const double OrdinaryLimit = 1e300;

    /// <summary>
    /// Per-axis closest coordinates of two intervals: when the intervals are disjoint the nearest faces, when they overlap the
    /// midpoint of the overlap (so touching/intersecting boxes report distance 0 with a shared witness point).
    /// </summary>
    /// <param name="minA">Start of the first interval.</param>
    /// <param name="maxA">End of the first interval.</param>
    /// <param name="minB">Start of the second interval.</param>
    /// <param name="maxB">End of the second interval.</param>
    /// <param name="a">The closest coordinate on the first interval.</param>
    /// <param name="b">The closest coordinate on the second interval.</param>
    public static void ClosestCoordinates(double minA, double maxA, double minB, double maxB, out double a, out double b)
    {
        if (minB > maxA)
        {
            a = maxA;
            b = minB;
        }
        else if (maxB < minA)
        {
            a = minA;
            b = maxB;
        }
        else
        {
            var mid = (Math.Max(minA, minB) + Math.Min(maxA, maxB)) / 2.0;
            a = mid;
            b = mid;
        }
    }

    /// <summary>The closest point on each of two boxes (the witness points whose distance is the distance between the boxes).</summary>
    /// <param name="a">The first box.</param>
    /// <param name="b">The second box.</param>
    /// <param name="ax">X on the first box.</param>
    /// <param name="ay">Y on the first box.</param>
    /// <param name="az">Z on the first box.</param>
    /// <param name="bx">X on the second box.</param>
    /// <param name="by">Y on the second box.</param>
    /// <param name="bz">Z on the second box.</param>
    public static void ClosestPoints(
        in AxisBox a,
        in AxisBox b,
        out double ax,
        out double ay,
        out double az,
        out double bx,
        out double by,
        out double bz)
    {
        ClosestCoordinates(a.MinX, a.MaxX, b.MinX, b.MaxX, out ax, out bx);
        ClosestCoordinates(a.MinY, a.MaxY, b.MinY, b.MaxY, out ay, out by);
        ClosestCoordinates(a.MinZ, a.MaxZ, b.MinZ, b.MaxZ, out az, out bz);
    }

    /// <summary>Shortest distance between two boxes (0 when they touch or overlap): the distance of their closest points.</summary>
    /// <param name="a">The first box.</param>
    /// <param name="b">The second box.</param>
    public static double Distance(in AxisBox a, in AxisBox b)
    {
        var dx = Gap(a.MinX, a.MaxX, b.MinX, b.MaxX);
        var dy = Gap(a.MinY, a.MaxY, b.MinY, b.MaxY);
        var dz = Gap(a.MinZ, a.MaxZ, b.MinZ, b.MaxZ);
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    /// <summary>
    /// True for a box whose numbers are finite and in order (min not above max). The nearest-box pruning is exact for these; any
    /// other box is measured against everything.
    /// </summary>
    /// <param name="box">The box.</param>
    public static bool IsOrdinary(in AxisBox box) =>
        InRange(box.MinX) && InRange(box.MinY) && InRange(box.MinZ) &&
        InRange(box.MaxX) && InRange(box.MaxY) && InRange(box.MaxZ) &&
        box.MinX <= box.MaxX && box.MinY <= box.MaxY && box.MinZ <= box.MaxZ;

    private static bool InRange(double value) => value >= -OrdinaryLimit && value <= OrdinaryLimit;

    // The difference of the two closest coordinates of an axis (b - a of ClosestCoordinates): the gap, signed, or 0 when overlapping.
    private static double Gap(double minA, double maxA, double minB, double maxB)
    {
        if (minB > maxA)
        {
            return minB - maxA;
        }

        if (maxB < minA)
        {
            return maxB - minA;
        }

        var mid = (Math.Max(minA, minB) + Math.Min(maxA, maxB)) / 2.0;
        return mid - mid;
    }
}

/// <summary>
/// Finds, for a box, the nearest of a fixed set of target boxes without measuring against all of them. Proximity.NearestDistance
/// compared every item with every target; here the targets are arranged once in a bounding-volume tree (each node holds the box of
/// everything below it), and a query measures the distance to a node's box first, which can only be less than or equal to the
/// distance to anything inside it, so whole branches that cannot beat the best distance so far are skipped.
/// <para>
/// The answer is exactly what comparing against every target in order gives: the same nearest target, the same distance to the
/// last bit (the distance of a pair is computed by <see cref="BoxGeometry.Distance"/> everywhere, and a bound is that same
/// function applied to a box that contains the target, which is never larger, operation by operation), and the lowest index among
/// targets at the same distance, as the strict "closer than the best so far" of a front-to-back scan keeps the first. Boxes with
/// non-finite or reversed coordinates are not pruned: such a target is measured against every query, and such a query against every
/// target.
/// </para>
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class NearestBoxIndex
{
    private const int LeafSize = 8;

    private readonly AxisBox[] _boxes;
    private readonly int[] _unordinary;

    // The tree over the ordinary targets. _order lists their indexes so that every node covers one run of it.
    private readonly int[] _order;
    private readonly AxisBox[] _nodeBox;
    private readonly int[] _nodeLeft;
    private readonly int[] _nodeRight;
    private readonly int[] _nodeStart;
    private readonly int[] _nodeCount;
    private readonly int[] _nodeLowestIndex;
    private int _nodes;

    /// <summary>Arranges the target boxes.</summary>
    /// <param name="targets">The target boxes; an index in the results is an index in this list.</param>
    public NearestBoxIndex(IReadOnlyList<AxisBox> targets)
    {
        if (targets == null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        _boxes = new AxisBox[targets.Count];
        var ordinary = new List<int>(targets.Count);
        var other = new List<int>();
        for (var i = 0; i < _boxes.Length; i++)
        {
            _boxes[i] = targets[i];
            (BoxGeometry.IsOrdinary(_boxes[i]) ? ordinary : other).Add(i);
        }

        _unordinary = other.ToArray();
        _order = ordinary.ToArray();
        var capacity = Math.Max(1, 2 * _order.Length);
        _nodeBox = new AxisBox[capacity];
        _nodeLeft = new int[capacity];
        _nodeRight = new int[capacity];
        _nodeStart = new int[capacity];
        _nodeCount = new int[capacity];
        _nodeLowestIndex = new int[capacity];
        if (_order.Length > 0)
        {
            Build(0, _order.Length, new double[_order.Length]);
        }
    }

    /// <summary>How many target boxes there are.</summary>
    public int Count => _boxes.Length;

    /// <summary>The nearest target to a box.</summary>
    /// <param name="query">The box to measure from.</param>
    /// <param name="index">The index of the nearest target; of several at the same distance, the lowest.</param>
    /// <param name="distance">The distance to it.</param>
    /// <returns>False when no target is at a finite distance (there are none, or every distance is infinite or not a number).</returns>
    public bool TryFindNearest(in AxisBox query, out int index, out double distance) =>
        TryFindNearest(query, out index, out distance, out _);

    // The same, also saying how many box distances were worked out (a box against a target or against the box of a node), which is
    // what the pruning saves: a scan of every target works out one per target.
    internal bool TryFindNearest(in AxisBox query, out int index, out double distance, out long evaluations)
    {
        var state = new SearchState { Best = double.PositiveInfinity, BestIndex = -1 };

        if (!BoxGeometry.IsOrdinary(query))
        {
            for (var i = 0; i < _boxes.Length; i++)
            {
                Consider(i, BoxGeometry.Distance(query, _boxes[i]), ref state);
            }

            state.Evaluations += _boxes.Length;
        }
        else
        {
            if (_order.Length > 0)
            {
                state.Evaluations++;
                Search(0, query, BoxGeometry.Distance(query, _nodeBox[0]), ref state);
            }

            foreach (var i in _unordinary)
            {
                Consider(i, BoxGeometry.Distance(query, _boxes[i]), ref state);
                state.Evaluations++;
            }
        }

        index = state.BestIndex;
        distance = state.Best;
        evaluations = state.Evaluations;
        return state.BestIndex >= 0;
    }

    private struct SearchState
    {
        public double Best;
        public int BestIndex;
        public long Evaluations;
    }

    // A candidate replaces the best only when it is closer, or as close with a lower index; an infinite or NaN distance never wins.
    private static void Consider(int index, double distance, ref SearchState state)
    {
        if (distance < state.Best)
        {
            state.Best = distance;
            state.BestIndex = index;
        }
        else if (distance == state.Best && state.BestIndex >= 0 && index < state.BestIndex)
        {
            state.BestIndex = index;
        }
    }

    private void Search(int node, in AxisBox query, double bound, ref SearchState state)
    {
        // Nothing below can be closer than the bound; at the same distance only a lower index could still win.
        if (bound > state.Best || (bound == state.Best && _nodeLowestIndex[node] > state.BestIndex))
        {
            return;
        }

        var left = _nodeLeft[node];
        if (left < 0)
        {
            var end = _nodeStart[node] + _nodeCount[node];
            for (var position = _nodeStart[node]; position < end; position++)
            {
                var target = _order[position];
                Consider(target, BoxGeometry.Distance(query, _boxes[target]), ref state);
            }

            state.Evaluations += _nodeCount[node];
            return;
        }

        var right = _nodeRight[node];
        var leftBound = BoxGeometry.Distance(query, _nodeBox[left]);
        var rightBound = BoxGeometry.Distance(query, _nodeBox[right]);
        state.Evaluations += 2;
        if (leftBound <= rightBound)
        {
            Search(left, query, leftBound, ref state);
            Search(right, query, rightBound, ref state);
        }
        else
        {
            Search(right, query, rightBound, ref state);
            Search(left, query, leftBound, ref state);
        }
    }

    // Builds the node for _order[start .. start + count) and returns its number.
    private int Build(int start, int count, double[] keys)
    {
        var node = _nodes++;
        _nodeStart[node] = start;
        _nodeCount[node] = count;
        _nodeLeft[node] = -1;
        _nodeRight[node] = -1;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        double centreMinX = double.MaxValue, centreMinY = double.MaxValue, centreMinZ = double.MaxValue;
        double centreMaxX = double.MinValue, centreMaxY = double.MinValue, centreMaxZ = double.MinValue;
        var lowest = int.MaxValue;
        for (var position = start; position < start + count; position++)
        {
            var target = _order[position];
            var box = _boxes[target];
            minX = Math.Min(minX, box.MinX);
            minY = Math.Min(minY, box.MinY);
            minZ = Math.Min(minZ, box.MinZ);
            maxX = Math.Max(maxX, box.MaxX);
            maxY = Math.Max(maxY, box.MaxY);
            maxZ = Math.Max(maxZ, box.MaxZ);
            var cx = (box.MinX + box.MaxX) / 2.0;
            var cy = (box.MinY + box.MaxY) / 2.0;
            var cz = (box.MinZ + box.MaxZ) / 2.0;
            centreMinX = Math.Min(centreMinX, cx);
            centreMinY = Math.Min(centreMinY, cy);
            centreMinZ = Math.Min(centreMinZ, cz);
            centreMaxX = Math.Max(centreMaxX, cx);
            centreMaxY = Math.Max(centreMaxY, cy);
            centreMaxZ = Math.Max(centreMaxZ, cz);
            lowest = Math.Min(lowest, target);
        }

        _nodeBox[node] = new AxisBox(minX, minY, minZ, maxX, maxY, maxZ);
        _nodeLowestIndex[node] = lowest;
        if (count <= LeafSize)
        {
            return node;
        }

        // Split at the median of the centres along the axis they are spread over the most.
        var spreadX = centreMaxX - centreMinX;
        var spreadY = centreMaxY - centreMinY;
        var spreadZ = centreMaxZ - centreMinZ;
        var axis = spreadX >= spreadY && spreadX >= spreadZ ? 0 : spreadY >= spreadZ ? 1 : 2;
        for (var position = start; position < start + count; position++)
        {
            var box = _boxes[_order[position]];
            keys[position] = axis == 0 ? box.MinX + box.MaxX : axis == 1 ? box.MinY + box.MaxY : box.MinZ + box.MaxZ;
        }

        Array.Sort(keys, _order, start, count);
        var half = count / 2;
        _nodeLeft[node] = Build(start, half, keys);
        _nodeRight[node] = Build(start + half, count - half, keys);
        return node;
    }
}
