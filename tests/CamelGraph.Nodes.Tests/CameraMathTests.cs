using System;
using CamelGraph.Nodes.Spatial;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Pins the camera maths behind Camera.SetStandardView and SavedViewpoint.Info: the standard-view
/// directions (Z up, +Y north), the eye placement, and the quaternion-to-view-direction conversion
/// in the Navisworks convention (identity looks down -Z with +Y up).
/// </summary>
public class CameraMathTests
{
    private const double Tolerance = 1e-9;

    private static void AssertVector((double X, double Y, double Z) actual, double x, double y, double z)
    {
        Assert.Equal(x, actual.X, 9);
        Assert.Equal(y, actual.Y, 9);
        Assert.Equal(z, actual.Z, 9);
    }

    // ------------------------------------------------------------ view names

    [Theory]
    [InlineData("top", "top")]
    [InlineData("TOP", "top")]
    [InlineData("  Front ", "front")]
    [InlineData("plan", "top")]
    [InlineData("isometric", "iso")]
    [InlineData("3D", "iso")]
    [InlineData("Rear", "back")]
    [InlineData("underside", "bottom")]
    [InlineData("west", "left")]
    [InlineData("east", "right")]
    public void NormalizeViewName_AcceptsCaseSpacesAndSynonyms(string input, string expected)
    {
        Assert.Equal(expected, CameraMath.NormalizeViewName(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sideways")]
    public void NormalizeViewName_RejectsUnknownNamesAndListsTheValidOnes(string input)
    {
        var error = Assert.Throws<ArgumentException>(() => CameraMath.NormalizeViewName(input));
        Assert.Contains("top, bottom, front, back, left, right, iso", error.Message);
    }

    [Fact]
    public void NormalizeViewName_Null_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => CameraMath.NormalizeViewName(null));
    }

    [Fact]
    public void StandardViewNames_AreTheSevenDropdownChoices()
    {
        Assert.Equal(new[] { "top", "bottom", "front", "back", "left", "right", "iso" }, CameraMath.StandardViewNames);
    }

    // ------------------------------------------------------------ directions

    [Theory]
    [InlineData("top", 0.0, 0.0, -1.0)]
    [InlineData("bottom", 0.0, 0.0, 1.0)]
    [InlineData("front", 0.0, 1.0, 0.0)]    // stands south of the model, looks north
    [InlineData("back", 0.0, -1.0, 0.0)]
    [InlineData("left", 1.0, 0.0, 0.0)]     // stands west of the model, looks east
    [InlineData("right", -1.0, 0.0, 0.0)]
    public void ViewDirection_AxisViews(string view, double x, double y, double z)
    {
        AssertVector(CameraMath.ViewDirection(view), x, y, z);
    }

    [Fact]
    public void ViewDirection_Iso_LooksFromTheSouthEastAbove()
    {
        var d = CameraMath.ViewDirection("iso");
        var k = 1.0 / Math.Sqrt(3.0);
        AssertVector(d, -k, k, -k);
        Assert.Equal(1.0, Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z)), 9);
    }

    [Fact]
    public void UpDirection_PlanViewsKeepNorthOrSouthAtTheTopAndEveryOtherViewIsZUp()
    {
        AssertVector(CameraMath.UpDirection("top"), 0, 1, 0);
        AssertVector(CameraMath.UpDirection("bottom"), 0, -1, 0);
        foreach (var view in new[] { "front", "back", "left", "right", "iso" })
        {
            AssertVector(CameraMath.UpDirection(view), 0, 0, 1);
        }
    }

    [Fact]
    public void UpDirection_IsAlwaysPerpendicularToTheViewDirection_SoAlignUpIsNeverDegenerate()
    {
        foreach (var view in CameraMath.StandardViewNames)
        {
            var d = CameraMath.ViewDirection(view);
            var u = CameraMath.UpDirection(view);
            var dot = (d.X * u.X) + (d.Y * u.Y) + (d.Z * u.Z);
            if (view == "iso")
            {
                // The isometric camera tilts: up is Z but the view is not level, so it is not perpendicular —
                // it just must not be parallel (AlignUp rolls the camera about the view axis).
                Assert.True(Math.Abs(dot) < 0.99, "iso up must not be parallel to its view direction");
            }
            else
            {
                Assert.Equal(0.0, dot, 9);
            }
        }
    }

    [Theory]
    [InlineData("top", 1.0, 0.0, 0.0)]      // X to the right of a plan
    [InlineData("bottom", 1.0, 0.0, 0.0)]   // X still to the right when seen from below
    [InlineData("front", 1.0, 0.0, 0.0)]
    [InlineData("back", -1.0, 0.0, 0.0)]
    [InlineData("left", 0.0, -1.0, 0.0)]
    [InlineData("right", 0.0, 1.0, 0.0)]
    public void RightHandSide_MatchesTheDocumentedConvention(string view, double x, double y, double z)
    {
        // screen right = forward x up (right-handed camera)
        var f = CameraMath.ViewDirection(view);
        var u = CameraMath.UpDirection(view);
        var right = (X: (f.Y * u.Z) - (f.Z * u.Y), Y: (f.Z * u.X) - (f.X * u.Z), Z: (f.X * u.Y) - (f.Y * u.X));
        AssertVector(right, x, y, z);
    }

    // ------------------------------------------------------------ eye position

    [Fact]
    public void EyePosition_TopView_IsAboveTheCentre()
    {
        AssertVector(CameraMath.EyePosition("top", 10, 20, 5, 100), 10, 20, 105);
    }

    [Fact]
    public void EyePosition_FrontView_IsSouthOfTheCentre()
    {
        AssertVector(CameraMath.EyePosition("front", 10, 20, 5, 100), 10, -80, 5);
    }

    [Fact]
    public void EyePosition_Iso_IsSouthEastAndAbove()
    {
        var eye = CameraMath.EyePosition("iso", 0, 0, 0, Math.Sqrt(3.0));
        AssertVector(eye, 1, -1, 1);
    }

    [Fact]
    public void EyePosition_EyeAlwaysLooksBackAtTheCentre()
    {
        foreach (var view in CameraMath.StandardViewNames)
        {
            var eye = CameraMath.EyePosition(view, 3, -4, 7, 25);
            var d = CameraMath.ViewDirection(view);
            Assert.Equal(3.0, eye.X + (d.X * 25), 9);
            Assert.Equal(-4.0, eye.Y + (d.Y * 25), 9);
            Assert.Equal(7.0, eye.Z + (d.Z * 25), 9);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EyePosition_RejectsABadDistance(double distance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraMath.EyePosition("top", 0, 0, 0, distance));
    }

    [Fact]
    public void SuggestedDistance_IsThreeRadiiOfTheBoundingSphere()
    {
        // a 6 x 8 x 0 box: diagonal 10, radius 5
        Assert.Equal(15.0, CameraMath.SuggestedDistance(6, 8, 0), 9);
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0)]
    [InlineData(double.NaN, 1.0, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0, 1.0)]
    public void SuggestedDistance_FallsBackToOneUnitForAnUnusableBox(double x, double y, double z)
    {
        Assert.Equal(1.0, CameraMath.SuggestedDistance(x, y, z), 9);
    }

    // ------------------------------------------------------------ rotation -> forward

    [Fact]
    public void ForwardFromRotation_Identity_LooksDownMinusZ()
    {
        AssertVector(CameraMath.ForwardFromRotation(0, 0, 0, 1), 0, 0, -1);
    }

    [Fact]
    public void ForwardFromRotation_QuarterTurnAboutX_LooksNorth_TheFrontViewOfAZUpModel()
    {
        var s = Math.Sqrt(0.5);
        AssertVector(CameraMath.ForwardFromRotation(s, 0, 0, s), 0, 1, 0);
    }

    [Fact]
    public void ForwardFromRotation_QuarterTurnAboutY_LooksEastOrWest()
    {
        // +90 degrees about Y turns -Z toward -X (right-hand rule).
        var s = Math.Sqrt(0.5);
        AssertVector(CameraMath.ForwardFromRotation(0, s, 0, s), -1, 0, 0);
    }

    [Fact]
    public void ForwardFromRotation_HalfTurnAboutX_LooksUp()
    {
        AssertVector(CameraMath.ForwardFromRotation(1, 0, 0, 0), 0, 0, 1);
    }

    [Fact]
    public void ForwardFromRotation_ToleratesAnUnnormalisedQuaternion()
    {
        AssertVector(CameraMath.ForwardFromRotation(2, 0, 0, 2), 0, 1, 0);
    }

    [Fact]
    public void ForwardFromRotation_ZeroQuaternion_IsTreatedAsIdentity()
    {
        AssertVector(CameraMath.ForwardFromRotation(0, 0, 0, 0), 0, 0, -1);
    }

    [Fact]
    public void ForwardFromRotation_AlwaysReturnsAUnitVector()
    {
        var rng = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            var f = CameraMath.ForwardFromRotation(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5);
            Assert.InRange(Math.Sqrt((f.X * f.X) + (f.Y * f.Y) + (f.Z * f.Z)), 1.0 - Tolerance, 1.0 + Tolerance);
        }
    }

    [Fact]
    public void ForwardFromRotation_AgreesWithTheViewFrustumConvention()
    {
        // ViewFrustum.Create takes the same camera as axis + angle; a box straight ahead of a camera built
        // from the quaternion's forward direction must be inside its frustum and one behind must not be.
        var angle = Math.PI / 2.0;
        var s = Math.Sin(angle / 2.0);
        var forward = CameraMath.ForwardFromRotation(s, 0, 0, Math.Cos(angle / 2.0)); // -> +Y
        var frustum = ViewFrustum.Create(0, 0, 0, 1, 0, 0, angle, orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);

        Assert.True(frustum.IntersectsBox(-0.1, 4.9, -0.1, 0.1, 5.1, 0.1));   // 5 units along +Y
        Assert.False(frustum.IntersectsBox(-0.1, -5.1, -0.1, 0.1, -4.9, 0.1)); // 5 units behind
        AssertVector(forward, 0, 1, 0);
    }

    // ------------------------------------------------------------ look-at point

    [Fact]
    public void LookAtPoint_MovesAlongTheViewDirectionByTheDistance()
    {
        var s = Math.Sqrt(0.5);
        AssertVector(CameraMath.LookAtPoint(1, 2, 3, s, 0, 0, s, 10), 1, 12, 3);
    }

    [Fact]
    public void LookAtPoint_Identity_LooksDownwards()
    {
        AssertVector(CameraMath.LookAtPoint(5, 5, 5, 0, 0, 0, 1, 2.5), 5, 5, 2.5);
    }

    [Theory]
    [InlineData(true, 12.5, 12.5)]
    [InlineData(false, 12.5, 1.0)]       // no stored focal distance: one unit
    [InlineData(true, 0.0, 1.0)]         // a zero focal distance is unusable
    [InlineData(true, -3.0, 1.0)]
    [InlineData(true, double.NaN, 1.0)]
    [InlineData(true, double.PositiveInfinity, 1.0)]
    public void LookAtDistance_UsesTheFocalDistanceOnlyWhenItIsUsable(bool has, double focal, double expected)
    {
        Assert.Equal(expected, CameraMath.LookAtDistance(has, focal), 9);
    }
}
