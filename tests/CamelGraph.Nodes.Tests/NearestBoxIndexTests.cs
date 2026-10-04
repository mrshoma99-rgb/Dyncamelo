using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Nodes.Spatial;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The geometry behind Proximity.NearestDistance. The reference is what the node did before: measure the item against every target
/// in order, keeping a target only when it is strictly closer than the best so far. The index must give the same nearest target
/// (the first of several at the same distance) and the same distance, bit for bit, whatever the boxes look like.
/// </summary>
public class NearestBoxIndexTests
{
    // ── The reference: the original pair-by-pair code, on plain numbers ──────

    private static void Closest(double minA, double maxA, double minB, double maxB, out double a, out double b)
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

    // BoxDistanceBetween of the old node: the closest points of the two boxes, then the distance of those two points.
    private static double OriginalDistance(AxisBox boxA, AxisBox boxB)
    {
        Closest(boxA.MinX, boxA.MaxX, boxB.MinX, boxB.MaxX, out var ax, out var bx);
        Closest(boxA.MinY, boxA.MaxY, boxB.MinY, boxB.MaxY, out var ay, out var by);
        Closest(boxA.MinZ, boxA.MaxZ, boxB.MinZ, boxB.MaxZ, out var az, out var bz);
        var dx = bx - ax;
        var dy = by - ay;
        var dz = bz - az;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static (int Index, double Distance) BruteForce(AxisBox query, IReadOnlyList<AxisBox> targets)
    {
        var nearest = double.PositiveInfinity;
        var index = -1;
        for (var i = 0; i < targets.Count; i++)
        {
            var distance = OriginalDistance(query, targets[i]);
            if (distance < nearest)
            {
                nearest = distance;
                index = i;
            }
        }

        return (index, nearest);
    }

    private static string Describe(AxisBox b) =>
        "[" + b.MinX + "," + b.MinY + "," + b.MinZ + " - " + b.MaxX + "," + b.MaxY + "," + b.MaxZ + "]";

    // Compares the index with the reference for every query; returns the number of queries checked.
    private static int AssertSameAsBruteForce(IReadOnlyList<AxisBox> targets, IReadOnlyList<AxisBox> queries)
    {
        var index = new NearestBoxIndex(targets);
        foreach (var query in queries)
        {
            var expected = BruteForce(query, targets);
            var found = index.TryFindNearest(query, out var actualIndex, out var actualDistance);

            var same = found == (expected.Index >= 0)
                && actualIndex == expected.Index
                && BitConverter.DoubleToInt64Bits(actualDistance) == BitConverter.DoubleToInt64Bits(expected.Distance);
            Assert.True(
                same,
                "query " + Describe(query) + " over " + targets.Count + " targets: expected #" + expected.Index + " at " + expected.Distance +
                ", got #" + actualIndex + " at " + actualDistance);
        }

        return queries.Count;
    }

    // ── Generators ───────────────────────────────────────────────────────────

    private static AxisBox Box(double x, double y, double z, double sx, double sy, double sz) => new AxisBox(x, y, z, x + sx, y + sy, z + sz);

    private static List<AxisBox> Uniform(Random random, int count, double extent, double maxSize) =>
        Enumerable.Range(0, count)
            .Select(_ => Box(random.NextDouble() * extent, random.NextDouble() * extent, random.NextDouble() * extent,
                random.NextDouble() * maxSize, random.NextDouble() * maxSize, random.NextDouble() * maxSize))
            .ToList();

    // Whole numbers on a small grid: many boxes touch, many are at exactly the same distance, many are at distance 0.
    private static List<AxisBox> Grid(Random random, int count, int cells, int maxSize) =>
        Enumerable.Range(0, count)
            .Select(_ => Box(random.Next(cells), random.Next(cells), random.Next(cells),
                random.Next(maxSize + 1), random.Next(maxSize + 1), random.Next(maxSize + 1)))
            .ToList();

    private static List<AxisBox> Clustered(Random random, int count)
    {
        var centres = Enumerable.Range(0, 5).Select(_ => (X: random.NextDouble() * 1000, Y: random.NextDouble() * 1000, Z: random.NextDouble() * 50)).ToList();
        return Enumerable.Range(0, count)
            .Select(_ =>
            {
                var c = centres[random.Next(centres.Count)];
                return Box(c.X + (random.NextDouble() * 20), c.Y + (random.NextDouble() * 20), c.Z + (random.NextDouble() * 5),
                    random.NextDouble() * 2, random.NextDouble() * 2, random.NextDouble() * 2);
            })
            .ToList();
    }

    // Long thin boxes (beams, pipes, railings) in the three directions.
    private static List<AxisBox> LongAndThin(Random random, int count) =>
        Enumerable.Range(0, count)
            .Select(_ =>
            {
                var length = 50 + (random.NextDouble() * 500);
                var thin = 0.05 + (random.NextDouble() * 0.3);
                var x = random.NextDouble() * 300;
                var y = random.NextDouble() * 300;
                var z = random.NextDouble() * 30;
                switch (random.Next(3))
                {
                    case 0: return Box(x, y, z, length, thin, thin);
                    case 1: return Box(x, y, z, thin, length, thin);
                    default: return Box(x, y, z, thin, thin, length);
                }
            })
            .ToList();

    private static readonly double[] OddNumbers =
    {
        double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e301, -1e301, double.MaxValue, 0.0, -0.0,
    };

    // Boxes with the kind of numbers a model never has: NaN, infinities, reversed corners, enormous coordinates.
    private static AxisBox Odd(Random random, Func<AxisBox> ordinary)
    {
        var box = ordinary();
        var values = new[] { box.MinX, box.MinY, box.MinZ, box.MaxX, box.MaxY, box.MaxZ };
        switch (random.Next(3))
        {
            case 0:
                values[random.Next(6)] = OddNumbers[random.Next(OddNumbers.Length)];
                break;
            case 1:
                (values[0], values[3]) = (values[3], values[0] - 1); // reversed on X
                break;
            default:
                values[random.Next(6)] = OddNumbers[random.Next(OddNumbers.Length)];
                values[random.Next(6)] = OddNumbers[random.Next(OddNumbers.Length)];
                break;
        }

        return new AxisBox(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void DistanceIsTheDistanceOfTheClosestPoints_AndZeroWhenTouching()
    {
        var unit = new AxisBox(0, 0, 0, 1, 1, 1);
        Assert.Equal(0, BoxGeometry.Distance(unit, new AxisBox(0.5, 0.5, 0.5, 2, 2, 2)));
        Assert.Equal(0, BoxGeometry.Distance(unit, new AxisBox(1, 0, 0, 2, 1, 1))); // a shared face
        Assert.Equal(0, BoxGeometry.Distance(unit, new AxisBox(1, 1, 1, 2, 2, 2))); // a shared corner
        Assert.Equal(2, BoxGeometry.Distance(unit, new AxisBox(3, 0, 0, 4, 1, 1)));
        Assert.Equal(5, BoxGeometry.Distance(unit, new AxisBox(4, 5, 1, 5, 6, 2))); // 3 along X, 4 along Y
        Assert.Equal(5, BoxGeometry.Distance(new AxisBox(4, 5, 1, 5, 6, 2), unit)); // the same the other way round
    }

    [Fact]
    public void ClosestPointsAreThoseOfTheOriginalRule()
    {
        BoxGeometry.ClosestPoints(new AxisBox(0, 0, 0, 1, 1, 1), new AxisBox(3, 0.5, 0.25, 4, 2, 0.75), out var ax, out var ay, out var az, out var bx, out var by, out var bz);

        Assert.Equal(new[] { 1.0, 0.75, 0.5 }, new[] { ax, ay, az });          // nearest face on X, middle of the overlap on Y and Z
        Assert.Equal(new[] { 3.0, 0.75, 0.5 }, new[] { bx, by, bz });
    }

    [Fact]
    public void DistanceEqualsTheOriginalPairCodeBitForBit()
    {
        var random = new Random(8);
        var boxes = Uniform(random, 60, 100, 8).Concat(Grid(random, 60, 6, 2)).ToList();
        foreach (var a in boxes)
        {
            foreach (var b in boxes)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(OriginalDistance(a, b)), BitConverter.DoubleToInt64Bits(BoxGeometry.Distance(a, b)));
            }
        }
    }

    [Fact]
    public void NoTargetsAndOnlyUnusableTargetsFindNothing()
    {
        var none = new NearestBoxIndex(new AxisBox[0]);
        Assert.False(none.TryFindNearest(new AxisBox(0, 0, 0, 1, 1, 1), out var index, out var distance));
        Assert.Equal(-1, index);
        Assert.True(double.IsPositiveInfinity(distance));
        Assert.Equal(0, none.Count);

        var nan = new AxisBox(double.NaN, 0, 0, double.NaN, 1, 1);
        AssertSameAsBruteForce(new[] { nan, nan }, new[] { new AxisBox(0, 0, 0, 1, 1, 1), nan });
    }

    [Fact]
    public void OneTargetAndTwoTargets()
    {
        var one = new[] { new AxisBox(5, 5, 5, 6, 6, 6) };
        var index = new NearestBoxIndex(one);
        Assert.True(index.TryFindNearest(new AxisBox(0, 0, 0, 1, 1, 1), out var found, out var distance));
        Assert.Equal(0, found);
        Assert.Equal(Math.Sqrt(48), distance);

        AssertSameAsBruteForce(
            new[] { new AxisBox(0, 0, 0, 1, 1, 1), new AxisBox(0, 0, 0, 1, 1, 1) },
            new[] { new AxisBox(0.5, 0.5, 0.5, 2, 2, 2), new AxisBox(9, 9, 9, 10, 10, 10) });
    }

    [Fact]
    public void OfSeveralTargetsAtTheSameDistanceTheFirstWins()
    {
        // Five identical boxes, all at distance 2 from the query, listed after a farther one; and zero-distance twins.
        var targets = new List<AxisBox> { new AxisBox(50, 0, 0, 51, 1, 1) };
        for (int i = 0; i < 40; i++)
        {
            targets.Add(new AxisBox(3, 0, 0, 4, 1, 1));
        }

        var index = new NearestBoxIndex(targets);
        Assert.True(index.TryFindNearest(new AxisBox(0, 0, 0, 1, 1, 1), out var found, out var distance));
        Assert.Equal(1, found);
        Assert.Equal(2, distance);

        Assert.True(index.TryFindNearest(new AxisBox(3.5, 0.5, 0.5, 3.6, 0.6, 0.6), out found, out distance));
        Assert.Equal(1, found);
        Assert.Equal(0, distance);
    }

    [Fact]
    public void RandomBoxesInASpace_GiveTheSameNearestAndDistanceAsComparingWithEveryTarget()
    {
        var random = new Random(1234);
        var checkedQueries = 0;
        foreach (var count in new[] { 0, 1, 2, 3, 8, 9, 10, 17, 33, 100, 500, 2000 })
        {
            for (int round = 0; round < 4; round++)
            {
                var targets = Uniform(random, count, round % 2 == 0 ? 100 : 20, round < 2 ? 5 : 15);
                var queries = Uniform(random, 60, round % 2 == 0 ? 120 : 25, 6);
                checkedQueries += AssertSameAsBruteForce(targets, queries);
            }
        }

        Assert.True(checkedQueries > 2000);
    }

    [Fact]
    public void BoxesOnAWholeNumberGrid_WithManyTiesAndTouchingBoxes_GiveTheSameAnswer()
    {
        var random = new Random(99);
        for (int round = 0; round < 40; round++)
        {
            var targets = Grid(random, random.Next(1, 600), random.Next(2, 12), random.Next(0, 3));
            var queries = Grid(random, 80, 14, 2);
            AssertSameAsBruteForce(targets, queries);
        }
    }

    [Fact]
    public void ClusteredLongAndFlatBoxes_GiveTheSameAnswer()
    {
        var random = new Random(5);
        for (int round = 0; round < 6; round++)
        {
            AssertSameAsBruteForce(Clustered(random, 800), Uniform(random, 100, 1000, 10).Concat(Clustered(random, 100)).ToList());
            AssertSameAsBruteForce(LongAndThin(random, 600), Uniform(random, 100, 300, 3));
            // Zero-thickness boxes (a plane, a line, a point).
            var flat = Enumerable.Range(0, 300)
                .Select(_ => Box(random.NextDouble() * 50, random.NextDouble() * 50, random.NextDouble() * 50, random.Next(2) * 5.0, random.Next(2) * 5.0, 0))
                .ToList();
            AssertSameAsBruteForce(flat, Uniform(random, 100, 55, 2));
        }
    }

    [Fact]
    public void IdenticalTargets_AllTie_AndTheFirstWins()
    {
        var targets = Enumerable.Repeat(new AxisBox(10, 10, 10, 11, 11, 11), 200).ToList();
        AssertSameAsBruteForce(targets, new[] { new AxisBox(0, 0, 0, 1, 1, 1), new AxisBox(10.5, 10.5, 10.5, 12, 12, 12) });
    }

    [Fact]
    public void TargetsAndQueriesWithNonFiniteOrReversedNumbers_GiveTheSameAnswer()
    {
        var random = new Random(404);
        for (int round = 0; round < 60; round++)
        {
            var targets = Uniform(random, random.Next(1, 80), 50, 6);
            for (int i = 0; i < targets.Count; i++)
            {
                if (random.Next(5) == 0)
                {
                    var original = targets[i];
                    targets[i] = Odd(random, () => original);
                }
            }

            var queries = Uniform(random, 40, 60, 5);
            for (int i = 0; i < queries.Count; i++)
            {
                if (random.Next(4) == 0)
                {
                    var original = queries[i];
                    queries[i] = Odd(random, () => original);
                }
            }

            AssertSameAsBruteForce(targets, queries);
        }
    }

    [Fact]
    public void PruningSkipsMostOfTheTargets()
    {
        // 20 000 targets spread over a model, 200 items to measure from: comparing with every target works out 20 000 distances per
        // item; the tree works out a few hundred.
        var random = new Random(77);
        var targets = Uniform(random, 20000, 500, 4);
        var index = new NearestBoxIndex(targets);
        long total = 0;
        var queries = Uniform(random, 200, 500, 4);
        foreach (var query in queries)
        {
            index.TryFindNearest(query, out var found, out var distance, out var evaluations);
            total += evaluations;
            var expected = BruteForce(query, targets);
            Assert.Equal(expected.Index, found);
            Assert.Equal(expected.Distance, distance);
        }

        var perItem = total / queries.Count;
        Assert.True(perItem < targets.Count / 20, "distances worked out per item: " + perItem + " of " + targets.Count);
    }

    [Fact]
    public void PruningAlsoHelpsWithManyTouchingBoxes()
    {
        // A grid of unit boxes that all touch their neighbours: lots of zero distances and ties.
        var targets = new List<AxisBox>();
        for (int x = 0; x < 30; x++)
        {
            for (int y = 0; y < 30; y++)
            {
                for (int z = 0; z < 10; z++)
                {
                    targets.Add(new AxisBox(x, y, z, x + 1, y + 1, z + 1));
                }
            }
        }

        var random = new Random(3);
        var index = new NearestBoxIndex(targets);
        long total = 0;
        var queries = Uniform(random, 100, 35, 3);
        foreach (var query in queries)
        {
            index.TryFindNearest(query, out var found, out var distance, out var evaluations);
            total += evaluations;
            var expected = BruteForce(query, targets);
            Assert.Equal(expected.Index, found);
            Assert.Equal(expected.Distance, distance);
        }

        Assert.True(total / queries.Count < targets.Count / 5, "distances worked out per item: " + (total / queries.Count) + " of " + targets.Count);
    }

    [Fact]
    public void ANullTargetListThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new NearestBoxIndex(null!));
    }
}
