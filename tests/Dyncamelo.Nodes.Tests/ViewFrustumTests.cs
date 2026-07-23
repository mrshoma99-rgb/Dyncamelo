using System;
using Dyncamelo.Nodes.Spatial;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The camera-frustum containment core behind Viewpoint.VisibleItems.
/// Convention under test: identity rotation looks down −Z with +Y up
/// (Navisworks camera space); perspective limits are tan(half-angle),
/// expressed as half extents at focal distance 1.
/// </summary>
public class ViewFrustumTests
{
    // 45° vertical half-angle, square aspect: tan = 1 at focal 1.
    private static ViewFrustum IdentityPerspective() =>
        ViewFrustum.Create(0, 0, 0, 0, 0, 0, 0, orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);

    [Fact]
    public void Perspective_BoxInFront_IsInsideAndContained()
    {
        var frustum = IdentityPerspective();
        Assert.True(frustum.IntersectsBox(-1, -1, -6, 1, 1, -4));
        Assert.True(frustum.ContainsBox(-1, -1, -6, 1, 1, -4));
    }

    [Fact]
    public void Perspective_BoxBehindCamera_IsOutside()
    {
        var frustum = IdentityPerspective();
        Assert.False(frustum.IntersectsBox(-1, -1, 4, 1, 1, 6));
    }

    [Fact]
    public void Perspective_BoxFarOffToTheSide_IsOutside()
    {
        var frustum = IdentityPerspective();
        // At depth 5 the frustum spans ±5; a box at x 20..22 is well outside.
        Assert.False(frustum.IntersectsBox(20, -1, -6, 22, 1, -4));
    }

    [Fact]
    public void Perspective_BoxStraddlingTheEdge_IntersectsButIsNotContained()
    {
        var frustum = IdentityPerspective();
        // At depth 5 the right edge is at x = 5: a box spanning 4..8 crosses it.
        Assert.True(frustum.IntersectsBox(4, -1, -6, 8, 1, -4));
        Assert.False(frustum.ContainsBox(4, -1, -6, 8, 1, -4));
    }

    [Fact]
    public void Perspective_HugeBoxSurroundingTheFrustum_CountsAsIntersecting()
    {
        // All corners are outside every single plane's cone, but no single
        // plane rejects all of them — the conservative test must keep it.
        var frustum = IdentityPerspective();
        Assert.True(frustum.IntersectsBox(-100, -100, -101, 100, 100, -1));
        Assert.False(frustum.ContainsBox(-100, -100, -101, 100, 100, -1));
    }

    [Fact]
    public void Perspective_RotatedCamera_LooksAlongMinusX()
    {
        // +90° about +Y turns the −Z view direction towards −X.
        var frustum = ViewFrustum.Create(
            0, 0, 0, axisX: 0, axisY: 1, axisZ: 0, angle: Math.PI / 2,
            orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);

        Assert.True(frustum.IntersectsBox(-6, -1, -1, -4, 1, 1));   // ahead
        Assert.False(frustum.IntersectsBox(4, -1, -1, 6, 1, 1));    // behind
        Assert.False(frustum.IntersectsBox(-1, -1, -6, 1, 1, -4));  // the old ahead is now off-axis
    }

    [Fact]
    public void Perspective_TranslatedCamera_TestsRelativeToPosition()
    {
        var frustum = ViewFrustum.Create(
            10, 20, 30, 0, 0, 0, 0, orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);

        Assert.True(frustum.IntersectsBox(9, 19, 24, 11, 21, 26));   // in front of (10,20,30)
        Assert.False(frustum.IntersectsBox(9, 19, 34, 11, 21, 36));  // behind it
    }

    [Fact]
    public void Orthographic_LateralLimitsAreConstant_RegardlessOfDepth()
    {
        var frustum = ViewFrustum.Create(
            0, 0, 0, 0, 0, 0, 0, orthographic: true, halfWidth: 2, halfHeight: 3, focalDistance: 1);

        Assert.True(frustum.IntersectsBox(-1, -2, -50, 1, 2, -48));   // deep but inside extents
        Assert.False(frustum.IntersectsBox(4, -1, -50, 6, 1, -48));   // beyond half width even when deep
        Assert.True(frustum.ContainsBox(-2, -3, -10, 2, 3, -9));
        Assert.False(frustum.ContainsBox(-2, -3.5, -10, 2, 3, -9));   // pokes past half height
    }

    [Fact]
    public void ZeroAxisOrAngle_MeansIdentityRotation()
    {
        var byAxis = ViewFrustum.Create(0, 0, 0, 0, 0, 0, 1.23, orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);
        var byAngle = ViewFrustum.Create(0, 0, 0, 0, 1, 0, 0, orthographic: false, halfWidth: 1, halfHeight: 1, focalDistance: 1);
        Assert.True(byAxis.IntersectsBox(-1, -1, -6, 1, 1, -4));
        Assert.True(byAngle.IntersectsBox(-1, -1, -6, 1, 1, -4));
    }
}
