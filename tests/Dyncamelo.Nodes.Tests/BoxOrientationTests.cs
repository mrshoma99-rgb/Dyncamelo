using Dyncamelo.Nodes.Spatial;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the box-shape classification behind ClashResult.Orientation /
/// Clash.FilterByOrientation: slab / wall / riser / run / block with the
/// documented ratio thresholds, the slope measure, and the unordered pair
/// matching — the logic that tells a pipe-through-wall clash from a
/// pipe-through-floor one.
/// </summary>
public class BoxOrientationTests
{
    // ------------------------------------------------------- classification

    [Theory]
    [InlineData(10.0, 8.0, 0.3, "slab")]     // floor slab: thin in Z
    [InlineData(0.3, 10.0, 3.0, "wall")]     // wall along Y: thin in X
    [InlineData(10.0, 0.3, 3.0, "wall")]     // wall along X: thin in Y
    [InlineData(0.4, 0.4, 3.0, "riser")]     // column / riser: long in Z
    [InlineData(6.0, 0.2, 0.2, "run")]       // pipe run along X
    [InlineData(0.2, 6.0, 0.2, "run")]       // pipe run along Y
    [InlineData(1.0, 1.0, 1.0, "block")]     // cube: nothing dominates
    [InlineData(2.0, 1.5, 1.8, "block")]     // chunky equipment
    public void Classify_RecognizesTheFiveShapes(double dx, double dy, double dz, string expected)
    {
        Assert.Equal(expected, BoxOrientation.Classify(dx, dy, dz));
    }

    [Fact]
    public void Classify_ThinDiagonalPipe_ReadsAsPlanar_TheDocumentedLimitation()
    {
        // A 45° pipe in the XZ plane has a thin axis-aligned box — box-based
        // classification cannot tell it from a wall. Pinned so the limitation
        // stays known instead of silently changing.
        Assert.Equal("wall", BoxOrientation.Classify(4.0, 0.2, 4.0));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, "block")]     // a point
    [InlineData(0.0, 0.0, 5.0, "riser")]     // degenerate vertical line
    [InlineData(5.0, 0.0, 0.0, "run")]       // degenerate horizontal line
    public void Classify_HandlesDegenerateBoxes(double dx, double dy, double dz, string expected)
    {
        Assert.Equal(expected, BoxOrientation.Classify(dx, dy, dz));
    }

    [Fact]
    public void Classify_ThresholdsMatchTheDocumentedConstants()
    {
        // Just inside/outside the planar ratio (smallest ≤ 25% of middle).
        Assert.Equal("slab", BoxOrientation.Classify(7.0, 4.0, 1.0));   // 1.0 == 0.25 × 4.0
        Assert.Equal("block", BoxOrientation.Classify(7.0, 4.0, 1.01)); // planar fails; 7 < 2 × 4 keeps it non-linear

        // Just inside/outside the linear ratio (largest ≥ 2× middle).
        Assert.Equal("run", BoxOrientation.Classify(8.0, 4.0, 3.9));
        Assert.Equal("block", BoxOrientation.Classify(7.9, 4.0, 3.9));
    }

    // ---------------------------------------------------------------- slope

    [Theory]
    [InlineData(1.0, 0.0, 0.0, 0.0)]
    [InlineData(0.0, 0.0, 1.0, 90.0)]
    [InlineData(1.0, 0.0, 1.0, 45.0)]
    public void SlopeDegrees_MeasuresFromHorizontal(double dx, double dy, double dz, double expected)
    {
        Assert.Equal(expected, BoxOrientation.SlopeDegrees(dx, dy, dz), 9);
    }

    [Fact]
    public void SlopeDegrees_ZeroDirection_IsNaN()
    {
        Assert.True(double.IsNaN(BoxOrientation.SlopeDegrees(0.0, 0.0, 0.0)));
    }

    // --------------------------------------------------------- pair matching

    [Fact]
    public void PairMatches_IsOrderInsensitive()
    {
        Assert.True(BoxOrientation.PairMatches("run", "wall", "wall", "run"));
        Assert.True(BoxOrientation.PairMatches("run", "wall", "run", "wall"));
        Assert.False(BoxOrientation.PairMatches("run", "slab", "run", "wall"));
    }

    [Fact]
    public void PairMatches_AnyAndEmpty_MatchEverything()
    {
        Assert.True(BoxOrientation.PairMatches("run", "wall", "any", "any"));
        Assert.True(BoxOrientation.PairMatches("run", "wall", "any", "wall"));
        Assert.True(BoxOrientation.PairMatches("riser", "slab", "", "slab"));
        Assert.False(BoxOrientation.PairMatches("run", "wall", "any", "slab"));
    }

    [Fact]
    public void PairMatches_TheMotivatingCase_PipeWallVersusPipeFloor()
    {
        // Same 90° crossing, different pairs — the whole point of the node.
        var pipeThroughWall = ("run", "wall");
        var pipeThroughFloor = ("run", "slab");

        Assert.True(BoxOrientation.PairMatches(pipeThroughWall.Item1, pipeThroughWall.Item2, "run", "wall"));
        Assert.False(BoxOrientation.PairMatches(pipeThroughFloor.Item1, pipeThroughFloor.Item2, "run", "wall"));
        Assert.True(BoxOrientation.PairMatches(pipeThroughFloor.Item1, pipeThroughFloor.Item2, "run", "slab"));
    }
}
