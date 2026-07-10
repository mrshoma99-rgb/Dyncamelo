using System;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class SpatialNodesTests
{
    private static DyncameloBoundingBox Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) =>
        new DyncameloBoundingBox(new DyncameloPoint(minX, minY, minZ), new DyncameloPoint(maxX, maxY, maxZ));

    // ------------------------------------------------- BoundingBox.Contains

    [Fact]
    public void Contains_PointInside_ReturnsTrue()
    {
        var box = Box(0, 0, 0, 10, 10, 10);
        Assert.True(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(5, 5, 5)));
    }

    [Theory]
    [InlineData(-0.001, 5, 5)]
    [InlineData(10.001, 5, 5)]
    [InlineData(5, -0.001, 5)]
    [InlineData(5, 10.001, 5)]
    [InlineData(5, 5, -0.001)]
    [InlineData(5, 5, 10.001)]
    public void Contains_PointOutsideAnyAxis_ReturnsFalse(double x, double y, double z)
    {
        var box = Box(0, 0, 0, 10, 10, 10);
        Assert.False(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(x, y, z)));
    }

    [Fact]
    public void Contains_PointsOnBoundary_CountAsInside()
    {
        var box = Box(0, 0, 0, 10, 10, 10);
        Assert.True(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(0, 0, 0)));    // corner
        Assert.True(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(10, 10, 10))); // opposite corner
        Assert.True(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(10, 5, 5)));   // face
    }

    [Fact]
    public void Contains_WorksWithNegativeCoordinates()
    {
        var box = Box(-10, -10, -10, -1, -1, -1);
        Assert.True(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(-5, -5, -5)));
        Assert.False(SpatialNodes.BoundingBoxContains(box, new DyncameloPoint(0, -5, -5)));
    }

    [Fact]
    public void Contains_NullArguments_Throw()
    {
        var box = Box(0, 0, 0, 1, 1, 1);
        Assert.Throws<ArgumentNullException>(() => SpatialNodes.BoundingBoxContains(null!, new DyncameloPoint(0, 0, 0)));
        Assert.Throws<ArgumentNullException>(() => SpatialNodes.BoundingBoxContains(box, null!));
    }

    // ------------------------------------------------------- Point.Translate

    [Fact]
    public void Translate_OffsetsByVectorComponents()
    {
        var moved = SpatialNodes.PointTranslate(new DyncameloPoint(1, 2, 3), new DyncameloVector(-1, 0.5, 2));
        Assert.Equal(new DyncameloPoint(0, 2.5, 5), moved);
    }

    [Fact]
    public void Translate_ZeroVector_ReturnsEqualPoint()
    {
        var moved = SpatialNodes.PointTranslate(new DyncameloPoint(1, 2, 3), new DyncameloVector(0, 0, 0));
        Assert.Equal(new DyncameloPoint(1, 2, 3), moved);
    }

    [Fact]
    public void Translate_ReturnsNewPoint_InputUnchanged()
    {
        var original = new DyncameloPoint(1, 1, 1);
        var moved = SpatialNodes.PointTranslate(original, new DyncameloVector(0, 0, 1));

        Assert.NotSame(original, moved);
        Assert.Equal(new DyncameloPoint(1, 1, 1), original);
        Assert.Equal(new DyncameloPoint(1, 1, 2), moved);
    }

    [Fact]
    public void Translate_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SpatialNodes.PointTranslate(null!, new DyncameloVector(0, 0, 0)));
        Assert.Throws<ArgumentNullException>(() => SpatialNodes.PointTranslate(new DyncameloPoint(0, 0, 0), null!));
    }
}
