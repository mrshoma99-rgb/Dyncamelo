using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Nodes.Spatial;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The clustering behind Clash.GroupResultsByProximity: a result joins the first cluster whose seed is within the radius,
/// otherwise it seeds a new one. The grid that spares most of the comparisons must give the answer of the comparison
/// with every seed, on any input.
/// </summary>
public class ProximityClustererTests
{
    // The old algorithm, literally.
    private static int[] EverySeed(double[] xs, double[] ys, double[] zs, double radius)
    {
        var seeds = new List<int>();
        var cluster = new int[xs.Length];
        for (int p = 0; p < xs.Length; p++)
        {
            var found = -1;
            for (int s = 0; s < seeds.Count; s++)
            {
                var dx = xs[seeds[s]] - xs[p];
                var dy = ys[seeds[s]] - ys[p];
                var dz = zs[seeds[s]] - zs[p];
                if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) <= radius)
                {
                    found = s;
                    break;
                }
            }

            if (found < 0)
            {
                found = seeds.Count;
                seeds.Add(p);
            }

            cluster[p] = found;
        }

        return cluster;
    }

    private static int[] Assign(double[] xs, double[] ys, double[] zs, double radius) => ProximityClusterer.Assign(xs, ys, zs, radius);

    [Fact]
    public void ThePointsOfAHotspotShareACluster()
    {
        var xs = new[] { 0.0, 1.0, 50.0, 0.5, 51.0 };
        var ys = new[] { 0.0, 0.0, 0.0, 0.5, 0.0 };
        var zs = new[] { 0.0, 0.0, 0.0, 0.0, 0.0 };

        Assert.Equal(new[] { 0, 0, 1, 0, 1 }, Assign(xs, ys, zs, 2.0));
    }

    [Fact]
    public void APointJoinsTheSeedItIsNearNotAClusterItChainsTo()
    {
        // 0 seeds, 1.5 is within 2 of it; 3.0 is within 2 of the point at 1.5 but not of the seed: it is a new cluster.
        var xs = new[] { 0.0, 1.5, 3.0 };
        var zeros = new[] { 0.0, 0.0, 0.0 };

        Assert.Equal(new[] { 0, 0, 1 }, Assign(xs, zeros, zeros, 2.0));
    }

    [Fact]
    public void ThePointWithinTheRadiusOfTwoSeedsJoinsTheFirst()
    {
        var xs = new[] { 0.0, 3.0, 1.5 };
        var zeros = new[] { 0.0, 0.0, 0.0 };

        Assert.Equal(new[] { 0, 1, 0 }, Assign(xs, zeros, zeros, 2.0));
    }

    [Fact]
    public void ADistanceExactlyTheRadiusIsWithin()
    {
        var xs = new[] { 0.0, 2.0, 2.0000001 };
        var zeros = new[] { 0.0, 0.0, 0.0 };

        Assert.Equal(new[] { 0, 0, 1 }, Assign(xs, zeros, zeros, 2.0));
    }

    [Fact]
    public void NoPointsMeansNoClusters()
    {
        Assert.Empty(Assign(new double[0], new double[0], new double[0], 1.0));
    }

    [Fact]
    public void CoordinateListsOfDifferentLengthAreRefused()
    {
        Assert.Throws<ArgumentException>(() => Assign(new[] { 1.0 }, new double[0], new[] { 1.0 }, 1.0));
    }

    [Fact]
    public void APointWithoutAPositionIsNearNothingAndNothingIsNearIt()
    {
        var xs = new[] { 0.0, double.NaN, 0.1, double.PositiveInfinity, double.NaN };
        var ys = new[] { 0.0, 0.0, 0.0, 0.0, double.NaN };
        var zs = new[] { 0.0, 0.0, 0.0, 0.0, 0.0 };

        var clusters = Assign(xs, ys, zs, 1.0);

        Assert.Equal(new[] { 0, 1, 0, 2, 3 }, clusters);
        Assert.Equal(EverySeed(xs, ys, zs, 1.0), clusters);
    }

    [Fact]
    public void AnAskedForNearnessReplacesTheStraightLineDistance()
    {
        // "near" means the same decade: the grid only chooses whom to ask.
        var xs = new[] { 1.0, 2.0, 11.0 };
        var zeros = new[] { 0.0, 0.0, 0.0 };

        var clusters = ProximityClusterer.Assign(xs, zeros, zeros, 100.0, (seed, point) => (int)(xs[seed] / 10) == (int)(xs[point] / 10));

        Assert.Equal(new[] { 0, 0, 1 }, clusters);
    }

    [Fact]
    public void ARandomCloudGivesTheSameClustersAsComparingEverySeed()
    {
        for (int seed = 0; seed < 150; seed++)
        {
            var random = new Random(seed);
            var count = random.Next(0, 300);
            var spread = new[] { 1.0, 5.0, 50.0, 1000.0 }[random.Next(4)];
            var radius = new[] { 0.01, 0.5, 2.0, 10.0, 300.0 }[random.Next(5)];
            var xs = new double[count];
            var ys = new double[count];
            var zs = new double[count];
            for (int i = 0; i < count; i++)
            {
                xs[i] = (random.NextDouble() - 0.5) * spread;
                ys[i] = (random.NextDouble() - 0.5) * spread;
                zs[i] = (random.NextDouble() - 0.5) * spread * (random.Next(3) == 0 ? 0.01 : 1.0);
            }

            Assert.True(
                EverySeed(xs, ys, zs, radius).SequenceEqual(Assign(xs, ys, zs, radius)),
                "seed " + seed + " (" + count + " points, spread " + spread + ", radius " + radius + ")");
        }
    }

    [Fact]
    public void PointsOnTheGridAndOnTheBoundariesGiveTheSameClustersAsComparingEverySeed()
    {
        // Integer coordinates with radius 1, 2, 3 put many pairs at exactly the radius, and on cell boundaries.
        foreach (var radius in new[] { 1.0, 2.0, 3.0, Math.Sqrt(2.0), Math.Sqrt(3.0) })
        {
            var xs = new List<double>();
            var ys = new List<double>();
            var zs = new List<double>();
            var random = new Random((int)(radius * 100));
            for (int i = 0; i < 400; i++)
            {
                xs.Add(random.Next(-6, 7));
                ys.Add(random.Next(-6, 7));
                zs.Add(random.Next(-2, 3));
            }

            Assert.Equal(
                EverySeed(xs.ToArray(), ys.ToArray(), zs.ToArray(), radius),
                Assign(xs.ToArray(), ys.ToArray(), zs.ToArray(), radius));
        }
    }

    [Fact]
    public void HugeCoordinatesOrAnUnusableRadiusFallBackToComparingEverySeed()
    {
        var xs = new[] { 1e15, 1e15 + 1.0, 1e15 + 40.0, -1e15 };
        var ys = new[] { 0.0, 0.0, 0.0, 0.0 };
        var zs = new[] { 0.0, 0.0, 0.0, 0.0 };

        Assert.Equal(EverySeed(xs, ys, zs, 2.0), Assign(xs, ys, zs, 2.0));
        Assert.Equal(EverySeed(xs, ys, zs, double.PositiveInfinity), Assign(xs, ys, zs, double.PositiveInfinity));
        Assert.Equal(EverySeed(xs, ys, zs, double.NaN), Assign(xs, ys, zs, double.NaN));
    }

    [Fact]
    public void SpreadOutPointsAreNotComparedWithEverySeed()
    {
        // 6,000 points on a 20 x 20 x 15 lattice, 10 units apart, radius 4: every point seeds its own cluster.
        var xs = new List<double>();
        var ys = new List<double>();
        var zs = new List<double>();
        for (int x = 0; x < 20; x++)
        {
            for (int y = 0; y < 20; y++)
            {
                for (int z = 0; z < 15; z++)
                {
                    xs.Add(x * 10.0);
                    ys.Add(y * 10.0);
                    zs.Add(z * 10.0);
                }
            }
        }

        long asked = 0;
        var clusters = ProximityClusterer.Assign(xs, ys, zs, 4.0, (seed, point) =>
        {
            asked++;
            var dx = xs[seed] - xs[point];
            var dy = ys[seed] - ys[point];
            var dz = zs[seed] - zs[point];
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) <= 4.0;
        });

        Assert.Equal(Enumerable.Range(0, xs.Count), clusters);
        Assert.True(asked < xs.Count * 10L, "asked " + asked + " times; comparing every seed would take " + ((long)xs.Count * (xs.Count - 1) / 2));
    }
}
