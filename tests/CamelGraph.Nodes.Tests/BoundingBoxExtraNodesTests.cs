using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Nodes;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// BoundingBoxExtraNodes: volume and areas, corners, expand, overlap, containment tests,
/// a box around points and translate.
/// </summary>
public class BoundingBoxExtraNodesTests
{
    private static CamelGraphPoint P(double x, double y, double z) => new CamelGraphPoint(x, y, z);

    private static CamelGraphVector V(double x, double y, double z) => new CamelGraphVector(x, y, z);

    private static CamelGraphBoundingBox Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) =>
        new CamelGraphBoundingBox(P(minX, minY, minZ), P(maxX, maxY, maxZ));

    // -------------------------------------------------------- Volume / areas

    [Fact]
    public void Volume_IsSizeXTimesSizeYTimesSizeZ()
    {
        Assert.Equal(24d, BoundingBoxExtraNodes.Volume(Box(0, 0, 0, 2, 3, 4)), 12);
        Assert.Equal(1d, BoundingBoxExtraNodes.Volume(Box(0, 0, 0, 1, 1, 1)), 12);
        Assert.Equal(24d, BoundingBoxExtraNodes.Volume(Box(-5, 10, -1, -3, 13, 3)), 12);   // away from the origin
    }

    [Fact]
    public void Volume_OfAFlatOrPointBox_IsZero()
    {
        Assert.Equal(0d, BoundingBoxExtraNodes.Volume(Box(0, 0, 5, 10, 10, 5)));
        Assert.Equal(0d, BoundingBoxExtraNodes.Volume(Box(1, 2, 3, 1, 2, 3)));
    }

    [Fact]
    public void SurfaceArea_IsTheSumOfTheSixFaces()
    {
        // 2 x 3 x 4: faces 2*3, 3*4, 2*4, each twice -> 2 * (6 + 12 + 8)
        Assert.Equal(52d, BoundingBoxExtraNodes.SurfaceArea(Box(0, 0, 0, 2, 3, 4)), 12);
        Assert.Equal(6d, BoundingBoxExtraNodes.SurfaceArea(Box(0, 0, 0, 1, 1, 1)), 12);
    }

    [Fact]
    public void SurfaceArea_OfAFlatBox_IsTwiceTheFace()
    {
        Assert.Equal(200d, BoundingBoxExtraNodes.SurfaceArea(Box(0, 0, 0, 10, 10, 0)), 12);
        Assert.Equal(0d, BoundingBoxExtraNodes.SurfaceArea(Box(4, 4, 4, 4, 4, 4)));
    }

    [Fact]
    public void Footprint_IsSizeXTimesSizeY_IgnoringHeight()
    {
        Assert.Equal(6d, BoundingBoxExtraNodes.Footprint(Box(0, 0, 0, 2, 3, 4)), 12);
        Assert.Equal(6d, BoundingBoxExtraNodes.Footprint(Box(0, 0, -100, 2, 3, 100)), 12);
        Assert.Equal(0d, BoundingBoxExtraNodes.Footprint(Box(0, 0, 0, 0, 5, 5)));
    }

    [Fact]
    public void Measurements_AreConsistentWithBoundingBoxSize()
    {
        var box = Box(-1.5, 2, 0.25, 4, 7.5, 3.25);
        var size = GeometryNodes.BoundingBoxSize(box);
        var x = (double)size["sizeX"];
        var y = (double)size["sizeY"];
        var z = (double)size["sizeZ"];

        Assert.Equal(x * y * z, BoundingBoxExtraNodes.Volume(box), 12);
        Assert.Equal(x * y, BoundingBoxExtraNodes.Footprint(box), 12);
        Assert.Equal(2 * (x * y + y * z + z * x), BoundingBoxExtraNodes.SurfaceArea(box), 12);
    }

    [Fact]
    public void Measurements_NullBox_NameTheNodeAndPort()
    {
        foreach (var (name, call) in new (string, Func<double>)[]
        {
            ("BoundingBox.Volume", () => BoundingBoxExtraNodes.Volume(null!)),
            ("BoundingBox.SurfaceArea", () => BoundingBoxExtraNodes.SurfaceArea(null!)),
            ("BoundingBox.Footprint", () => BoundingBoxExtraNodes.Footprint(null!)),
        })
        {
            var error = Assert.Throws<ArgumentNullException>(() => call());
            Assert.Contains(name, error.Message);
            Assert.Contains("'boundingBox'", error.Message);
        }
    }

    // ------------------------------------------------------------------ Corners

    [Fact]
    public void Corners_AreTheEightPoints_BottomFaceCounterClockwiseThenTop()
    {
        var corners = BoundingBoxExtraNodes.Corners(Box(0, 0, 0, 1, 2, 3));

        Assert.Equal(
            new[]
            {
                P(0, 0, 0), P(1, 0, 0), P(1, 2, 0), P(0, 2, 0),
                P(0, 0, 3), P(1, 0, 3), P(1, 2, 3), P(0, 2, 3),
            },
            corners);
    }

    [Fact]
    public void Corners_FirstIsMin_AndTopFaceIsTheBottomFaceRaised()
    {
        var box = Box(-2, 5, 10, 4, 9, 20);
        var corners = BoundingBoxExtraNodes.Corners(box);

        Assert.Equal(8, corners.Count);
        Assert.Equal(box.Min, corners[0]);
        Assert.Equal(box.Max, corners[6]);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(box.Min.Z, corners[i].Z);
            Assert.Equal(box.Max.Z, corners[i + 4].Z);
            Assert.Equal(corners[i].X, corners[i + 4].X);
            Assert.Equal(corners[i].Y, corners[i + 4].Y);
        }
    }

    [Fact]
    public void Corners_BottomFaceRunsCounterClockwiseSeenFromAbove()
    {
        var c = BoundingBoxExtraNodes.Corners(Box(0, 0, 0, 3, 2, 1));

        // Shoelace formula over the XY projection of corners 0-3: positive = counter-clockwise.
        double twiceArea = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % 4];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }

        Assert.Equal(6d, twiceArea / 2d, 12);
    }

    [Fact]
    public void Corners_AreAllDistinct_AllOnTheBox_AndFitBackIntoTheSameBox()
    {
        var box = Box(-1, -2, -3, 4, 5, 6);
        var corners = BoundingBoxExtraNodes.Corners(box);

        Assert.Equal(8, corners.Distinct().Count());
        Assert.All(corners, c => Assert.True(SpatialNodes.BoundingBoxContains(box, c)));
        Assert.Equal(box, BoundingBoxExtraNodes.FromPoints(corners.Cast<object?>().ToList()));
    }

    [Fact]
    public void Corners_OfAFlatBox_RepeatPoints()
    {
        var corners = BoundingBoxExtraNodes.Corners(Box(0, 0, 2, 5, 5, 2));
        Assert.Equal(8, corners.Count);
        Assert.Equal(4, corners.Distinct().Count());
    }

    [Fact]
    public void Corners_NullBox_Throws()
    {
        Assert.Contains("BoundingBox.Corners", Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Corners(null!)).Message);
    }

    // ------------------------------------------------------------------- Expand

    [Fact]
    public void Expand_GrowsEverySide()
    {
        var grown = BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 10, 10, 10), 2);

        Assert.Equal(P(-2, -2, -2), grown.Min);
        Assert.Equal(P(12, 12, 12), grown.Max);
        Assert.Equal(14d, GeometryNodes.BoundingBoxSize(grown)["sizeX"]);
        Assert.Equal(Box(0, 0, 0, 10, 10, 10).Center, grown.Center);
    }

    [Fact]
    public void Expand_NegativeAmountShrinks()
    {
        var shrunk = BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 10, 20, 30), -2);

        Assert.Equal(P(2, 2, 2), shrunk.Min);
        Assert.Equal(P(8, 18, 28), shrunk.Max);
    }

    [Fact]
    public void Expand_ZeroLeavesTheBoxAlone()
    {
        var box = Box(1, 2, 3, 4, 5, 6);
        Assert.Equal(box, BoundingBoxExtraNodes.Expand(box, 0));
    }

    [Fact]
    public void Expand_ThenShrinkByTheSameAmount_RestoresTheBox()
    {
        var box = Box(1, 2, 3, 4, 5, 6);
        Assert.Equal(box, BoundingBoxExtraNodes.Expand(BoundingBoxExtraNodes.Expand(box, 0.5), -0.5));
    }

    [Fact]
    public void Expand_ShrinkingToExactlyZeroSize_IsAllowed()
    {
        // The Z size is 10, so -5 on each side collapses it to a flat box at z = 5.
        var flat = BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 20, 20, 10), -5);

        Assert.Equal(P(5, 5, 5), flat.Min);
        Assert.Equal(P(15, 15, 5), flat.Max);
        Assert.Equal(0d, BoundingBoxExtraNodes.Volume(flat));
    }

    [Fact]
    public void Expand_ShrinkingPastZero_NamesTheAxesAndTheirSizes()
    {
        var error = Assert.Throws<ArgumentException>(() => BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 20, 20, 10), -5.5));

        Assert.Contains("BoundingBox.Expand", error.Message);
        Assert.Contains("5.5", error.Message);
        Assert.Contains("zero size along Z (size 10)", error.Message);
        Assert.DoesNotContain("X (size", error.Message);
        Assert.Contains("smaller negative amount", error.Message);
    }

    [Fact]
    public void Expand_ShrinkingPastZeroOnSeveralAxes_ListsEachOne()
    {
        var error = Assert.Throws<ArgumentException>(() => BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 2, 4, 100), -3));

        Assert.Contains("X (size 2)", error.Message);
        Assert.Contains("Y (size 4)", error.Message);
        Assert.DoesNotContain("Z (size", error.Message);
    }

    [Fact]
    public void Expand_ErrorNumbersAreInvariantAcrossCultures()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;

            var error = Assert.Throws<ArgumentException>(() => BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 4, 4, 4), -2.5));
            Assert.Contains("2.5", error.Message);
            Assert.DoesNotContain("2,5", error.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Expand_NonFiniteAmount_IsAnError(double amount)
    {
        var error = Assert.Throws<ArgumentException>(() => BoundingBoxExtraNodes.Expand(Box(0, 0, 0, 1, 1, 1), amount));
        Assert.Contains("BoundingBox.Expand", error.Message);
        Assert.Contains("finite", error.Message);
    }

    [Fact]
    public void Expand_NullBox_Throws()
    {
        var error = Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Expand(null!, 1));
        Assert.Contains("BoundingBox.Expand", error.Message);
        Assert.Contains("'boundingBox'", error.Message);
    }

    // ------------------------------------------------------------------ Overlap

    [Fact]
    public void Overlap_IsTheSharedRegion()
    {
        var overlap = BoundingBoxExtraNodes.Overlap(Box(0, 0, 0, 10, 10, 10), Box(5, 6, 7, 20, 20, 20));

        Assert.Equal(Box(5, 6, 7, 10, 10, 10), overlap);
    }

    [Fact]
    public void Overlap_IsSymmetric()
    {
        var a = Box(0, 0, 0, 10, 10, 10);
        var b = Box(-5, 4, 2, 6, 30, 8);

        Assert.Equal(BoundingBoxExtraNodes.Overlap(a, b), BoundingBoxExtraNodes.Overlap(b, a));
    }

    [Fact]
    public void Overlap_OfABoxInsideAnother_IsTheInnerBox()
    {
        var outer = Box(0, 0, 0, 100, 100, 100);
        var inner = Box(10, 20, 30, 40, 50, 60);

        Assert.Equal(inner, BoundingBoxExtraNodes.Overlap(outer, inner));
        Assert.Equal(inner, BoundingBoxExtraNodes.Overlap(inner, outer));
        Assert.Equal(outer, BoundingBoxExtraNodes.Overlap(outer, outer));
    }

    [Fact]
    public void Overlap_BoxesThatOnlyTouch_GiveAFlatBox()
    {
        var overlap = BoundingBoxExtraNodes.Overlap(Box(0, 0, 0, 10, 10, 10), Box(10, 2, 3, 20, 8, 9));

        Assert.Equal(Box(10, 2, 3, 10, 8, 9), overlap);
        Assert.Equal(0d, BoundingBoxExtraNodes.Volume(overlap));
    }

    [Fact]
    public void Overlap_CornerTouch_GivesASinglePoint()
    {
        var overlap = BoundingBoxExtraNodes.Overlap(Box(0, 0, 0, 1, 1, 1), Box(1, 1, 1, 2, 2, 2));
        Assert.Equal(Box(1, 1, 1, 1, 1, 1), overlap);
    }

    [Fact]
    public void Overlap_DisjointBoxes_ExplainThemselvesAndPointToIntersects()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BoundingBoxExtraNodes.Overlap(Box(0, 0, 0, 1, 1, 1), Box(5, 5, 5, 6, 6, 6)));

        Assert.Contains("BoundingBox.Overlap", error.Message);
        Assert.Contains("do not overlap", error.Message);
        Assert.Contains("BoundingBox.Intersects", error.Message);
    }

    [Theory]
    [InlineData(5, 0, 0)]
    [InlineData(0, 5, 0)]
    [InlineData(0, 0, 5)]
    [InlineData(-5, 0, 0)]
    [InlineData(0, -5, 0)]
    [InlineData(0, 0, -5)]
    public void Overlap_SeparatedOnASingleAxis_StillThrows(double dx, double dy, double dz)
    {
        var a = Box(0, 0, 0, 1, 1, 1);
        var b = Box(dx, dy, dz, dx + 1, dy + 1, dz + 1);

        Assert.Throws<InvalidOperationException>(() => BoundingBoxExtraNodes.Overlap(a, b));
    }

    [Fact]
    public void Overlap_ThrowsExactlyWhenIntersectsIsFalse()
    {
        var boxes = new[]
        {
            Box(0, 0, 0, 4, 4, 4), Box(2, 2, 2, 6, 6, 6), Box(4, 4, 4, 8, 8, 8), Box(5, 0, 0, 9, 4, 4),
            Box(-3, -3, -3, -1, -1, -1), Box(1, 1, 1, 2, 2, 2), Box(0, 0, 4.0001, 4, 4, 9), Box(-10, -10, -10, 10, 10, 10),
        };

        foreach (var a in boxes)
        {
            foreach (var b in boxes)
            {
                if (GeometryNodes.BoundingBoxIntersects(a, b))
                {
                    var overlap = BoundingBoxExtraNodes.Overlap(a, b);
                    Assert.True(BoundingBoxExtraNodes.ContainsBox(a, overlap));
                    Assert.True(BoundingBoxExtraNodes.ContainsBox(b, overlap));
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => BoundingBoxExtraNodes.Overlap(a, b));
                }
            }
        }
    }

    [Fact]
    public void Overlap_NullArguments_Throw()
    {
        var first = Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Overlap(null!, Box(0, 0, 0, 1, 1, 1)));
        Assert.Contains("BoundingBox.Overlap", first.Message);
        Assert.Contains("'a'", first.Message);
        Assert.Contains("'b'", Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Overlap(Box(0, 0, 0, 1, 1, 1), null!)).Message);
    }

    // ------------------------------------------------------------- ContainsBox

    [Fact]
    public void ContainsBox_InnerFullyInside_IsTrue()
    {
        Assert.True(BoundingBoxExtraNodes.ContainsBox(Box(0, 0, 0, 10, 10, 10), Box(2, 3, 4, 5, 6, 7)));
    }

    [Fact]
    public void ContainsBox_EqualBoxesAndSharedFaces_CountAsInside()
    {
        var outer = Box(0, 0, 0, 10, 10, 10);

        Assert.True(BoundingBoxExtraNodes.ContainsBox(outer, outer));
        Assert.True(BoundingBoxExtraNodes.ContainsBox(outer, Box(0, 0, 0, 5, 5, 5)));        // sharing the min corner
        Assert.True(BoundingBoxExtraNodes.ContainsBox(outer, Box(5, 5, 5, 10, 10, 10)));     // sharing the max corner
        Assert.True(BoundingBoxExtraNodes.ContainsBox(outer, Box(3, 3, 10, 6, 6, 10)));      // flat box lying on the top face
    }

    [Theory]
    [InlineData(-1, 0, 0, 5, 5, 5)]
    [InlineData(0, -1, 0, 5, 5, 5)]
    [InlineData(0, 0, -1, 5, 5, 5)]
    [InlineData(0, 0, 0, 11, 5, 5)]
    [InlineData(0, 0, 0, 5, 11, 5)]
    [InlineData(0, 0, 0, 5, 5, 11)]
    [InlineData(-5, -5, -5, 15, 15, 15)]      // the "inner" box is bigger
    [InlineData(20, 20, 20, 30, 30, 30)]      // disjoint
    public void ContainsBox_AnythingSticksOut_IsFalse(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Assert.False(BoundingBoxExtraNodes.ContainsBox(Box(0, 0, 0, 10, 10, 10), Box(minX, minY, minZ, maxX, maxY, maxZ)));
    }

    [Fact]
    public void ContainsBox_IsNotSymmetric()
    {
        var big = Box(0, 0, 0, 10, 10, 10);
        var small = Box(1, 1, 1, 2, 2, 2);

        Assert.True(BoundingBoxExtraNodes.ContainsBox(big, small));
        Assert.False(BoundingBoxExtraNodes.ContainsBox(small, big));
    }

    [Fact]
    public void ContainsBox_AnExpandedBoxContainsTheOriginal()
    {
        var box = Box(1, 2, 3, 4, 5, 6);
        Assert.True(BoundingBoxExtraNodes.ContainsBox(BoundingBoxExtraNodes.Expand(box, 0.25), box));
        Assert.False(BoundingBoxExtraNodes.ContainsBox(BoundingBoxExtraNodes.Expand(box, -0.25), box));
    }

    [Fact]
    public void ContainsBox_NullArguments_NameTheNodeAndPort()
    {
        var noOuter = Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.ContainsBox(null!, Box(0, 0, 0, 1, 1, 1)));
        Assert.Contains("BoundingBox.ContainsBox", noOuter.Message);
        Assert.Contains("'outer'", noOuter.Message);
        Assert.Contains("'inner'", Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.ContainsBox(Box(0, 0, 0, 1, 1, 1), null!)).Message);
    }

    // --------------------------------------------------------------- FromPoints

    [Fact]
    public void FromPoints_IsTheSmallestBoxAroundThePoints()
    {
        var box = BoundingBoxExtraNodes.FromPoints(new List<object?> { P(1, 5, -2), P(4, 2, 9), P(-3, 7, 0) });

        Assert.Equal(P(-3, 2, -2), box.Min);
        Assert.Equal(P(4, 7, 9), box.Max);
    }

    [Fact]
    public void FromPoints_ASinglePoint_GivesAZeroSizeBox()
    {
        var box = BoundingBoxExtraNodes.FromPoints(new List<object?> { P(2, 3, 4) });

        Assert.Equal(P(2, 3, 4), box.Min);
        Assert.Equal(P(2, 3, 4), box.Max);
        Assert.Equal(0d, BoundingBoxExtraNodes.Volume(box));
    }

    [Fact]
    public void FromPoints_EveryPointEndsUpInsideTheBox()
    {
        var points = new List<object?> { P(0.5, 0.5, 0.5), P(-9, 3, 2), P(7, -4, 1), P(2, 2, -6), P(2, 2, 6) };
        var box = BoundingBoxExtraNodes.FromPoints(points);

        Assert.All(points, p => Assert.True(SpatialNodes.BoundingBoxContains(box, (CamelGraphPoint)p!)));
    }

    [Fact]
    public void FromPoints_OrderDoesNotMatter_AndDuplicatesAreHarmless()
    {
        var a = BoundingBoxExtraNodes.FromPoints(new List<object?> { P(0, 0, 0), P(1, 2, 3), P(1, 2, 3) });
        var b = BoundingBoxExtraNodes.FromPoints(new List<object?> { P(1, 2, 3), P(0, 0, 0) });

        Assert.Equal(a, b);
    }

    [Fact]
    public void FromPoints_AgreesWithBoundingBoxUnionOnPoints()
    {
        var points = new List<object?> { P(1, 5, -2), P(4, 2, 9), P(-3, 7, 0) };
        Assert.Equal(GeometryNodes.BoundingBoxUnion(points), BoundingBoxExtraNodes.FromPoints(points));
    }

    [Fact]
    public void FromPoints_NullItem_NamesTheNodeAndIndex()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            BoundingBoxExtraNodes.FromPoints(new List<object?> { P(0, 0, 0), null }));

        Assert.Contains("BoundingBox.FromPoints", error.Message);
        Assert.Contains("item 1", error.Message);
        Assert.Contains("null", error.Message);
    }

    [Fact]
    public void FromPoints_NonPointItem_NamesTheIndex()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            BoundingBoxExtraNodes.FromPoints(new List<object?> { "x", P(0, 0, 0) }));

        Assert.Contains("item 0", error.Message);
        Assert.Contains("\"x\"", error.Message);
    }

    [Fact]
    public void FromPoints_EmptyList_IsAnError()
    {
        var error = Assert.Throws<ArgumentException>(() => BoundingBoxExtraNodes.FromPoints(new List<object?>()));
        Assert.Contains("BoundingBox.FromPoints", error.Message);
        Assert.Contains("at least one point", error.Message);
    }

    [Fact]
    public void FromPoints_NullList_IsAnError()
    {
        var error = Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.FromPoints(null!));
        Assert.Contains("BoundingBox.FromPoints", error.Message);
        Assert.Contains("'points'", error.Message);
    }

    // ---------------------------------------------------------------- Translate

    [Fact]
    public void Translate_MovesBothCorners_KeepingTheSize()
    {
        var moved = BoundingBoxExtraNodes.Translate(Box(0, 0, 0, 2, 3, 4), V(10, -5, 1));

        Assert.Equal(Box(10, -5, 1, 12, -2, 5), moved);
        Assert.Equal(BoundingBoxExtraNodes.Volume(Box(0, 0, 0, 2, 3, 4)), BoundingBoxExtraNodes.Volume(moved), 12);
    }

    [Fact]
    public void Translate_ByZero_IsTheSameBox_AndByTheNegatedVectorUndoesIt()
    {
        var box = Box(1, 2, 3, 4, 5, 6);
        var offset = V(7, -8, 9);

        Assert.Equal(box, BoundingBoxExtraNodes.Translate(box, V(0, 0, 0)));
        Assert.Equal(box, BoundingBoxExtraNodes.Translate(BoundingBoxExtraNodes.Translate(box, offset), VectorNodes.Negate(offset)));
    }

    [Fact]
    public void Translate_MovesTheCenterByTheVector()
    {
        var box = Box(0, 0, 0, 2, 2, 2);
        var moved = BoundingBoxExtraNodes.Translate(box, V(5, 6, 7));

        Assert.Equal(SpatialNodes.PointTranslate(box.Center, V(5, 6, 7)), moved.Center);
    }

    [Fact]
    public void Translate_NullArguments_NameTheNodeAndPort()
    {
        var noBox = Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Translate(null!, V(1, 1, 1)));
        Assert.Contains("BoundingBox.Translate", noBox.Message);
        Assert.Contains("'boundingBox'", noBox.Message);
        Assert.Contains("'offset'", Assert.Throws<ArgumentNullException>(() => BoundingBoxExtraNodes.Translate(Box(0, 0, 0, 1, 1, 1), null!)).Message);
    }

    // ---------------------------------------------- registration and engine

    private static NodeRegistry CreateRegistry()
    {
        var registry = NodeRegistry.CreateDefault();
        NodeLibrary.RegisterAll(registry);
        return registry;
    }

    private static NodeDefinition Definition(NodeRegistry registry, string name) =>
        registry.Definitions.Single(d => d.Name == name);

    [Fact]
    public void Registration_EveryNodeIsImported_WithDescriptionAndTags_InTheGeometryCategory()
    {
        var registry = CreateRegistry();
        var names = new[]
        {
            "BoundingBox.Volume", "BoundingBox.SurfaceArea", "BoundingBox.Footprint", "BoundingBox.Corners",
            "BoundingBox.Expand", "BoundingBox.Overlap", "BoundingBox.ContainsBox",
            "BoundingBox.FromPoints", "BoundingBox.Translate",
        };

        foreach (var name in names)
        {
            var definition = Definition(registry, name);   // Single: the name is unique in the whole library
            Assert.Equal("Geometry", definition.Category);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name + " has no description");
            Assert.NotEmpty(definition.SearchTags);
        }
    }

    [Fact]
    public void Registration_MeasuringAndTestingNodesAreInfo_BuildingNodesAreCreate()
    {
        var registry = CreateRegistry();

        foreach (var name in new[] { "BoundingBox.Volume", "BoundingBox.SurfaceArea", "BoundingBox.Footprint", "BoundingBox.Corners", "BoundingBox.ContainsBox" })
        {
            Assert.Equal(NodeFunction.Info, Definition(registry, name).Function);
        }

        foreach (var name in new[] { "BoundingBox.Expand", "BoundingBox.Overlap", "BoundingBox.FromPoints", "BoundingBox.Translate" })
        {
            Assert.Equal(NodeFunction.Create, Definition(registry, name).Function);
        }
    }

    [Fact]
    public void Registration_FromPointsTakesSeveralWiresOnOneSocket()
    {
        var input = Definition(CreateRegistry(), "BoundingBox.FromPoints").Inputs.Single();

        Assert.Equal("points", input.Name);
        Assert.True(input.MultiInput);
    }

    [Fact]
    public void Engine_FromPointsThenVolume_RunsWithSeveralWiresOnOneSocket()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var two = new NumberInputNode { Value = 2 };
        var three = new NumberInputNode { Value = 3 };
        var four = new NumberInputNode { Value = 4 };
        var origin = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var far = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var fromPoints = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.FromPoints"));
        var volume = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.Volume"));
        var footprint = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.Footprint"));
        foreach (var node in new NodeModel[] { two, three, four, origin, far, fromPoints, volume, footprint })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(two.OutPorts[0], far.InPorts[0]).Success);
        Assert.True(graph.Connect(three.OutPorts[0], far.InPorts[1]).Success);
        Assert.True(graph.Connect(four.OutPorts[0], far.InPorts[2]).Success);
        Assert.True(graph.Connect(origin.OutPorts[0], fromPoints.InPorts[0]).Success);
        Assert.True(graph.Connect(far.OutPorts[0], fromPoints.InPorts[0]).Success);   // second wire into the same socket
        Assert.True(graph.Connect(fromPoints.OutPorts[0], volume.InPorts[0]).Success);
        Assert.True(graph.Connect(fromPoints.OutPorts[0], footprint.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, fromPoints.State);
        Assert.Equal(24d, (double)volume.OutPorts[0].Value!, 12);
        Assert.Equal(6d, (double)footprint.OutPorts[0].Value!, 12);
    }

    [Fact]
    public void Engine_DisjointOverlap_BecomesAnErrorNodePointingAtIntersects()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var farAway = new NumberInputNode { Value = 50 };
        var minA = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var maxA = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var minB = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var maxB = new ZeroTouchNodeModel(Definition(registry, "Point.ByCoordinates"));
        var boxA = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.ByCorners"));
        var boxB = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.ByCorners"));
        var overlap = new ZeroTouchNodeModel(Definition(registry, "BoundingBox.Overlap"));
        foreach (var node in new NodeModel[] { farAway, minA, maxA, minB, maxB, boxA, boxB, overlap })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(farAway.OutPorts[0], minB.InPorts[0]).Success);
        Assert.True(graph.Connect(farAway.OutPorts[0], maxB.InPorts[0]).Success);
        Assert.True(graph.Connect(minA.OutPorts[0], boxA.InPorts[0]).Success);
        Assert.True(graph.Connect(maxA.OutPorts[0], boxA.InPorts[1]).Success);
        Assert.True(graph.Connect(minB.OutPorts[0], boxB.InPorts[0]).Success);
        Assert.True(graph.Connect(maxB.OutPorts[0], boxB.InPorts[1]).Success);
        Assert.True(graph.Connect(boxA.OutPorts[0], overlap.InPorts[0]).Success);
        Assert.True(graph.Connect(boxB.OutPorts[0], overlap.InPorts[1]).Success);

        // Box A is a single point at the origin and box B a single point at x = 50: they do not meet.
        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, overlap.State);
        Assert.Contains("BoundingBox.Intersects", overlap.StateMessage);
    }
}
