using System;
using CamelGraph.Nodes.Spatial;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Pins the matrix maths behind ModelItem.Scale (uniform scale about a point) and ModelItem.MoveTo
/// (bounding-box centre onto a target): row-major 4x4, acting on column vectors, the same layout as
/// ModelItem.SetTransform.
/// </summary>
public class ItemTransformMathTests
{
    private static void AssertPoint((double X, double Y, double Z) actual, double x, double y, double z)
    {
        Assert.Equal(x, actual.X, 9);
        Assert.Equal(y, actual.Y, 9);
        Assert.Equal(z, actual.Z, 9);
    }

    // ------------------------------------------------------------ scale factor

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RequireScaleFactor_RejectsZeroNegativeAndNonFiniteFactors(double factor)
    {
        var error = Assert.Throws<ArgumentException>(() => ItemTransformMath.RequireScaleFactor(factor));
        Assert.Contains("positive scale factor", error.Message);
        Assert.Contains("ModelItem.SetTransform", error.Message); // tells the user how to mirror
    }

    [Theory]
    [InlineData(1e-9)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(25.4)]
    [InlineData(1e6)]
    public void RequireScaleFactor_AcceptsPositiveFactors(double factor)
    {
        ItemTransformMath.RequireScaleFactor(factor);
    }

    [Fact]
    public void RequireScaleFactor_MessageUsesInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            System.Globalization.CultureInfo.CurrentCulture = comma;

            var error = Assert.Throws<ArgumentException>(() => ItemTransformMath.RequireScaleFactor(-1.5));
            Assert.Contains("-1.5", error.Message);
            Assert.DoesNotContain("-1,5", error.Message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(1.0 + 1e-15, true)]
    [InlineData(1.001, false)]
    [InlineData(0.999, false)]
    public void IsIdentityScale_OnlyForOne(double factor, bool expected)
    {
        Assert.Equal(expected, ItemTransformMath.IsIdentityScale(factor));
    }

    // ------------------------------------------------------------ scale about a point

    [Fact]
    public void ScaleAboutPoint_HasTheDocumentedLayout()
    {
        var m = ItemTransformMath.ScaleAboutPoint(2.0, 10, 20, 30);
        Assert.Equal(16, m.Length);
        // linear part: 2 on the diagonal; translation (1 - f) * c = -c at indices 3, 7, 11; bottom row 0 0 0 1
        Assert.Equal(new[] { 2.0, 0, 0, -10, 0, 2.0, 0, -20, 0, 0, 2.0, -30, 0, 0, 0, 1 }, m);
    }

    [Fact]
    public void ScaleAboutPoint_KeepsTheCentreFixed()
    {
        var m = ItemTransformMath.ScaleAboutPoint(3.5, 12.5, -4, 7);
        AssertPoint(ItemTransformMath.TransformPoint(m, 12.5, -4, 7), 12.5, -4, 7);
    }

    [Fact]
    public void ScaleAboutPoint_MovesOtherPointsAwayFromTheCentreByTheFactor()
    {
        var m = ItemTransformMath.ScaleAboutPoint(2.0, 1, 1, 1);
        // a point 3 units from the centre along each axis ends 6 away
        AssertPoint(ItemTransformMath.TransformPoint(m, 4, 1, 1), 7, 1, 1);
        AssertPoint(ItemTransformMath.TransformPoint(m, 1, -2, 1), 1, -5, 1);
        AssertPoint(ItemTransformMath.TransformPoint(m, 0, 0, 0), -1, -1, -1);
    }

    [Fact]
    public void ScaleAboutPoint_ShrinkingPullsPointsTowardTheCentre()
    {
        var m = ItemTransformMath.ScaleAboutPoint(0.5, 0, 0, 0);
        AssertPoint(ItemTransformMath.TransformPoint(m, 8, -4, 2), 4, -2, 1);
    }

    [Fact]
    public void ScaleAboutPoint_AboutTheOrigin_HasNoTranslation()
    {
        var m = ItemTransformMath.ScaleAboutPoint(4.0, 0, 0, 0);
        Assert.Equal(0.0, m[3]);
        Assert.Equal(0.0, m[7]);
        Assert.Equal(0.0, m[11]);
    }

    [Fact]
    public void ScaleAboutPoint_FactorOne_IsTheIdentity()
    {
        var m = ItemTransformMath.ScaleAboutPoint(1.0, 5, 6, 7);
        Assert.Equal(new[] { 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1 }, m);
    }

    [Fact]
    public void ScaleAboutPoint_IsUniform_ScalesBoxExtentsByTheFactor()
    {
        var m = ItemTransformMath.ScaleAboutPoint(3.0, 5, 5, 5);
        var lo = ItemTransformMath.TransformPoint(m, 0, 0, 0);
        var hi = ItemTransformMath.TransformPoint(m, 2, 4, 6);
        Assert.Equal(6.0, hi.X - lo.X, 9);
        Assert.Equal(12.0, hi.Y - lo.Y, 9);
        Assert.Equal(18.0, hi.Z - lo.Z, 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ScaleAboutPoint_RejectsABadFactor(double factor)
    {
        Assert.Throws<ArgumentException>(() => ItemTransformMath.ScaleAboutPoint(factor, 0, 0, 0));
    }

    // ------------------------------------------------------------ box centre / move

    [Fact]
    public void BoxCentre_IsTheMidpoint()
    {
        AssertPoint(ItemTransformMath.BoxCentre(0, 0, 0, 10, 20, 30), 5, 10, 15);
        AssertPoint(ItemTransformMath.BoxCentre(-4, -2, 1, 2, 2, 3), -1, 0, 2);
    }

    [Fact]
    public void MoveDelta_IsTargetMinusCurrent()
    {
        AssertPoint(ItemTransformMath.MoveDelta(1, 2, 3, 11, 2, -7), 10, 0, -10);
    }

    [Fact]
    public void MoveTo_PutsTheBoxCentreOnTheTarget()
    {
        // The MoveTo recipe end to end: centre of a box, delta to the target, translation matrix.
        var centre = ItemTransformMath.BoxCentre(0, 0, 0, 4, 6, 8);
        var delta = ItemTransformMath.MoveDelta(centre.X, centre.Y, centre.Z, 100, 200, 300);
        var m = ItemTransformMath.Translation(delta.X, delta.Y, delta.Z);
        AssertPoint(ItemTransformMath.TransformPoint(m, centre.X, centre.Y, centre.Z), 100, 200, 300);
        // a corner moves by the same offset
        AssertPoint(ItemTransformMath.TransformPoint(m, 0, 0, 0), 98, 197, 296);
    }

    [Fact]
    public void Translation_HasTheDocumentedLayout()
    {
        Assert.Equal(new[] { 1.0, 0, 0, 5, 0, 1.0, 0, -6, 0, 0, 1.0, 7, 0, 0, 0, 1 }, ItemTransformMath.Translation(5, -6, 7));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, true)]
    [InlineData(1e-12, -1e-12, 0.0, true)]
    [InlineData(0.001, 0.0, 0.0, false)]
    [InlineData(0.0, 0.0, -5.0, false)]
    public void IsNegligibleMove_SkipsMovesBelowNanoUnits(double dx, double dy, double dz, bool expected)
    {
        Assert.Equal(expected, ItemTransformMath.IsNegligibleMove(dx, dy, dz));
    }

    // ------------------------------------------------------------ finite points

    [Fact]
    public void RequireFinitePoint_AcceptsOrdinaryPoints()
    {
        ItemTransformMath.RequireFinitePoint(1, -2, 3.5, "target");
    }

    [Theory]
    [InlineData(double.NaN, 0.0, 0.0)]
    [InlineData(0.0, double.PositiveInfinity, 0.0)]
    [InlineData(0.0, 0.0, double.NegativeInfinity)]
    public void RequireFinitePoint_RejectsNanAndInfinity_NamingThePoint(double x, double y, double z)
    {
        var error = Assert.Throws<ArgumentException>(() => ItemTransformMath.RequireFinitePoint(x, y, z, "target"));
        Assert.Contains("target", error.Message);
        Assert.Contains("finite", error.Message);
    }

    // ------------------------------------------------------------ TransformPoint

    [Fact]
    public void TransformPoint_RejectsAMatrixThatIsNotSixteenNumbers()
    {
        Assert.Throws<ArgumentException>(() => ItemTransformMath.TransformPoint(new double[9], 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => ItemTransformMath.TransformPoint(null!, 0, 0, 0));
    }
}
