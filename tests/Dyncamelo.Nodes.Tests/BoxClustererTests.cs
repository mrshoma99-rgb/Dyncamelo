using System.Collections.Generic;
using Dyncamelo.Nodes.Spatial;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The touching-boxes clustering core behind Proximity.Cluster: connected
/// components where "connected" means the box gap is at most the tolerance,
/// directly or through a chain.
/// </summary>
public class BoxClustererTests
{
    private static double[] Box(double x, double y, double z, double sizeX = 1, double sizeY = 1, double sizeZ = 1) =>
        new[] { x, y, z, x + sizeX, y + sizeY, z + sizeZ };

    [Fact]
    public void TwoLadders_ChainTouchingWithinEach_GapBetween()
    {
        // Ladder A: three boxes in a row, each touching only its neighbour.
        // Ladder B: two touching boxes, 5 units away.
        var boxes = new List<double[]?>
        {
            Box(0, 0, 0),
            Box(1, 0, 0),   // touches box 0 (shared face at x=1)
            Box(2, 0, 0),   // touches box 1, NOT box 0
            Box(10, 0, 0),
            Box(11, 0, 0),  // touches box 3
        };

        var ids = BoxClusterer.Cluster(boxes, tolerance: 0);
        Assert.Equal(new[] { 0, 0, 0, 1, 1 }, ids);
    }

    [Fact]
    public void Tolerance_DecidesWhetherAGapConnects()
    {
        var boxes = new List<double[]?> { Box(0, 0, 0), Box(1.5, 0, 0) }; // 0.5 gap

        Assert.Equal(new[] { 0, 1 }, BoxClusterer.Cluster(boxes, 0.1));
        Assert.Equal(new[] { 0, 0 }, BoxClusterer.Cluster(boxes, 0.6));
    }

    [Fact]
    public void GapOnAnyAxis_MustBeWithinTolerance()
    {
        // X ranges overlap, but the boxes are 2 apart in Z.
        var boxes = new List<double[]?> { Box(0, 0, 0), Box(0, 0, 3) };
        Assert.Equal(new[] { 0, 1 }, BoxClusterer.Cluster(boxes, 0.5));
        Assert.Equal(new[] { 0, 0 }, BoxClusterer.Cluster(boxes, 2.0));
    }

    [Fact]
    public void OverlappingBoxes_AlwaysCluster()
    {
        var boxes = new List<double[]?> { Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1) };
        Assert.Equal(new[] { 0, 0 }, BoxClusterer.Cluster(boxes, 0));
    }

    [Fact]
    public void ClusterNumbers_FollowFirstAppearanceInInputOrder()
    {
        // The spatially-leftmost box comes LAST in the input: numbering must
        // still start from the first input item, not from the sweep order.
        var boxes = new List<double[]?>
        {
            Box(50, 0, 0),
            Box(51, 0, 0),
            Box(0, 0, 0),
        };

        Assert.Equal(new[] { 0, 0, 1 }, BoxClusterer.Cluster(boxes, 0));
    }

    [Fact]
    public void MissingOrDegenerateBoxes_GetMinusOne_OthersUnaffected()
    {
        var boxes = new List<double[]?>
        {
            Box(0, 0, 0),
            null,
            new[] { 5.0, 0, 0 },               // wrong length
            new[] { 3.0, 0, 0, 2.0, 1, 1 },    // min > max
            Box(1, 0, 0),
        };

        Assert.Equal(new[] { 0, -1, -1, -1, 0 }, BoxClusterer.Cluster(boxes, 0));
    }

    [Fact]
    public void ManyIsolatedBoxes_EachTheirOwnCluster()
    {
        var boxes = new List<double[]?>();
        for (int i = 0; i < 20; i++)
        {
            boxes.Add(Box(i * 10, 0, 0));
        }

        var ids = BoxClusterer.Cluster(boxes, 1.0);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(i, ids[i]);
        }
    }
}
