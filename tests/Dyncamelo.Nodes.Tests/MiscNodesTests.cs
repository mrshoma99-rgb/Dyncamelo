using System;
using System.Globalization;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>Color, DateTime and Geometry node tests.</summary>
public class MiscNodesTests
{
    // ----------------------------------------------------------------- Color

    [Fact]
    public void ColorByArgb_BuildsColor_AlphaDefaultsToOpaque()
    {
        var color = ColorNodes.ByArgb(r: 10, g: 20, b: 30);
        Assert.Equal(255, color.A);
        Assert.Equal(10, color.R);
        Assert.Equal(20, color.G);
        Assert.Equal(30, color.B);
    }

    [Fact]
    public void ColorByArgb_ClampsChannelsToByteRange()
    {
        var color = ColorNodes.ByArgb(300, -5, 256, 128);
        Assert.Equal(255, color.A);
        Assert.Equal(0, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(128, color.B);
    }

    [Fact]
    public void DyncameloColor_ValueEquality()
    {
        Assert.Equal(new DyncameloColor(255, 1, 2, 3), new DyncameloColor(255, 1, 2, 3));
        Assert.NotEqual(new DyncameloColor(255, 1, 2, 3), new DyncameloColor(254, 1, 2, 3));
    }

    [Fact]
    public void ColorPickerNode_OutputsCurrentColor_AndClamps()
    {
        var node = new ColorPickerNode { R = 300, G = 40, B = -1 };
        var outputs = node.Evaluate(Array.Empty<object?>(), new Dyncamelo.Core.Execution.EvaluationContext());
        var color = Assert.IsType<DyncameloColor>(outputs[0]);
        Assert.Equal(new DyncameloColor(255, 255, 40, 0), color);
    }

    // -------------------------------------------------------------- DateTime

    [Fact]
    public void DateTimeNow_ReturnsCurrentLocalTime()
    {
        var before = DateTime.Now.AddMinutes(-1);
        var now = DateTimeNodes.Now();
        var after = DateTime.Now.AddMinutes(1);
        Assert.InRange(now, before, after);
    }

    [Fact]
    public void DateTimeFormat_UsesInvariantCulture()
    {
        var date = new DateTime(2026, 7, 9, 13, 45, 30);
        Assert.Equal("2026-07-09 13:45:30", DateTimeNodes.Format(date));
        Assert.Equal("09/Jul/2026", DateTimeNodes.Format(date, "dd/MMM/yyyy"));
    }

    [Fact]
    public void DateTimeFormat_EmptyFormat_FallsBackToDefault()
    {
        var date = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal("2026-01-02 03:04:05", DateTimeNodes.Format(date, ""));
    }

    // -------------------------------------------------------------- Geometry

    [Fact]
    public void PointByCoordinates_DefaultsToOrigin()
    {
        var origin = GeometryNodes.PointByCoordinates();
        Assert.Equal(new DyncameloPoint(0, 0, 0), origin);
    }

    [Fact]
    public void PointComponents_ReturnsMultiReturnDictionary()
    {
        var components = GeometryNodes.PointComponents(new DyncameloPoint(1, 2, 3));
        Assert.Equal(1d, components["x"]);
        Assert.Equal(2d, components["y"]);
        Assert.Equal(3d, components["z"]);
    }

    [Fact]
    public void BoundingBox_NormalizesCorners_AndComputesCenter()
    {
        var box = GeometryNodes.BoundingBoxByCorners(
            new DyncameloPoint(10, 0, 5),
            new DyncameloPoint(0, 4, -5));

        Assert.Equal(new DyncameloPoint(0, 0, -5), box.Min);
        Assert.Equal(new DyncameloPoint(10, 4, 5), box.Max);
        Assert.Equal(new DyncameloPoint(5, 2, 0), GeometryNodes.BoundingBoxCenter(box));
    }

    [Fact]
    public void GeometryNodes_NullInputs_ThrowHelpfulMessages()
    {
        Assert.Throws<ArgumentNullException>(() => GeometryNodes.PointComponents(null!));
        Assert.Throws<ArgumentNullException>(() => GeometryNodes.BoundingBoxCenter(null!));
    }

    [Fact]
    public void ValueTypes_FormatInvariantly()
    {
        Assert.Equal("Point(1.5, 2, 3)", new DyncameloPoint(1.5, 2, 3).ToString());
        Assert.Equal("Color(A=255, R=0, G=0, B=0)", new DyncameloColor(255, 0, 0, 0).ToString());
    }
}
