using System;
using System.Collections.Generic;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes.Spatial;

/// <summary>
/// Groups points into clusters around seed points, the way Clash.GroupResultsByProximity does: the points are taken
/// in order, and a point joins the FIRST cluster (the oldest seed) whose seed is within the radius; a point that is
/// within the radius of no seed becomes the seed of a new cluster. The answer is the same as comparing every point
/// with every seed, but the seeds are kept in a grid of radius-sized cells so a point is only compared with the
/// seeds of the 27 cells around it. Free of Navisworks types, so it is fully unit-testable.
/// </summary>
[IsVisibleInLibrary(false)]
public static class ProximityClusterer
{
    // The cells are a little larger than the radius, so rounding in x / cell can never put two points that are within
    // the radius two cells apart; with coordinates beyond MaxCellCoordinate the rounding error could eat that margin,
    // and the comparison with every seed is used instead.
    private const double CellMargin = 1e-6;
    private const double MaxCellCoordinate = 1e9;

    /// <summary>Assigns every point to a cluster.</summary>
    /// <param name="xs">The X of each point.</param>
    /// <param name="ys">The Y of each point.</param>
    /// <param name="zs">The Z of each point.</param>
    /// <param name="radius">The cluster radius, in the units of the coordinates.</param>
    /// <param name="isNear">
    /// Whether a point is within the radius of a seed: (index of the seed's point, index of the point). Null uses the straight-line
    /// distance. A seed that gives true must be within <paramref name="radius"/> (plus rounding) of the point; the grid only decides
    /// which seeds are worth asking about.
    /// </param>
    /// <returns>The cluster number of each point; clusters are numbered from 0 in the order their seeds appear.</returns>
    public static int[] Assign(
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        IReadOnlyList<double> zs,
        double radius,
        Func<int, int, bool>? isNear = null)
    {
        if (xs == null || ys == null || zs == null)
        {
            throw new ArgumentNullException(xs == null ? nameof(xs) : ys == null ? nameof(ys) : nameof(zs));
        }

        if (ys.Count != xs.Count || zs.Count != xs.Count)
        {
            throw new ArgumentException("The coordinate lists must have the same length.");
        }

        var near = isNear ?? ((seed, point) => Distance(xs, ys, zs, seed, point) <= radius);
        var cell = radius * (1.0 + CellMargin);
        if (!UseGrid(xs, ys, zs, cell))
        {
            return AssignByComparingEverySeed(xs.Count, near);
        }

        var clusterOf = new int[xs.Count];
        var seedPoint = new List<int>();
        var seedsByCell = new Dictionary<CellKey, List<int>>();
        for (int point = 0; point < xs.Count; point++)
        {
            if (!IsFinite(xs[point]) || !IsFinite(ys[point]) || !IsFinite(zs[point]))
            {
                // A point without a position is within the radius of nothing (and nothing is within it): its own cluster.
                clusterOf[point] = seedPoint.Count;
                seedPoint.Add(point);
                continue;
            }

            var home = new CellKey(xs[point], ys[point], zs[point], cell);
            var best = int.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!seedsByCell.TryGetValue(home.Offset(dx, dy, dz), out var seeds))
                        {
                            continue;
                        }

                        // Seeds are listed oldest first: the first near one is this cell's candidate for the oldest.
                        foreach (var seed in seeds)
                        {
                            if (seed >= best)
                            {
                                break;
                            }

                            if (near(seedPoint[seed], point))
                            {
                                best = seed;
                                break;
                            }
                        }
                    }
                }
            }

            if (best != int.MaxValue)
            {
                clusterOf[point] = best;
                continue;
            }

            var number = seedPoint.Count;
            seedPoint.Add(point);
            clusterOf[point] = number;
            if (!seedsByCell.TryGetValue(home, out var cellSeeds))
            {
                cellSeeds = new List<int>();
                seedsByCell[home] = cellSeeds;
            }

            cellSeeds.Add(number);
        }

        return clusterOf;
    }

    private static int[] AssignByComparingEverySeed(int count, Func<int, int, bool> near)
    {
        var clusterOf = new int[count];
        var seedPoint = new List<int>();
        for (int point = 0; point < count; point++)
        {
            var cluster = -1;
            for (int seed = 0; seed < seedPoint.Count; seed++)
            {
                if (near(seedPoint[seed], point))
                {
                    cluster = seed;
                    break;
                }
            }

            if (cluster < 0)
            {
                cluster = seedPoint.Count;
                seedPoint.Add(point);
            }

            clusterOf[point] = cluster;
        }

        return clusterOf;
    }

    // The grid needs a usable cell size and coordinates small enough for the cell numbers to be exact.
    private static bool UseGrid(IReadOnlyList<double> xs, IReadOnlyList<double> ys, IReadOnlyList<double> zs, double cell)
    {
        if (!IsFinite(cell) || cell <= 0.0)
        {
            return false;
        }

        var limit = MaxCellCoordinate * cell;
        for (int i = 0; i < xs.Count; i++)
        {
            if ((IsFinite(xs[i]) && Math.Abs(xs[i]) > limit) ||
                (IsFinite(ys[i]) && Math.Abs(ys[i]) > limit) ||
                (IsFinite(zs[i]) && Math.Abs(zs[i]) > limit))
            {
                return false;
            }
        }

        return true;
    }

    private static double Distance(IReadOnlyList<double> xs, IReadOnlyList<double> ys, IReadOnlyList<double> zs, int a, int b)
    {
        var dx = xs[a] - xs[b];
        var dy = ys[a] - ys[b];
        var dz = zs[a] - zs[b];
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private readonly struct CellKey : IEquatable<CellKey>
    {
        private readonly long _x;
        private readonly long _y;
        private readonly long _z;

        internal CellKey(double x, double y, double z, double cell)
            : this((long)Math.Floor(x / cell), (long)Math.Floor(y / cell), (long)Math.Floor(z / cell))
        {
        }

        private CellKey(long x, long y, long z)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        internal CellKey Offset(int dx, int dy, int dz) => new CellKey(_x + dx, _y + dy, _z + dz);

        public bool Equals(CellKey other) => _x == other._x && _y == other._y && _z == other._z;

        public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)(_x ^ (_x >> 32));
                hash = (hash * 397) ^ (int)(_y ^ (_y >> 32));
                hash = (hash * 397) ^ (int)(_z ^ (_z >> 32));
                return hash;
            }
        }
    }
}
