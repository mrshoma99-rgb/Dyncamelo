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
        Assert.Equal("Vector(1, 0, -2.5)", new DyncameloVector(1, 0, -2.5).ToString());
    }

    // ----------------------------------------------------------- Color (v0.2)

    [Theory]
    [InlineData("#FF8800", 255, 255, 136, 0)]
    [InlineData("ff8800", 255, 255, 136, 0)] // no '#', lowercase
    [InlineData("#80FF8800", 128, 255, 136, 0)] // AARRGGBB
    [InlineData(" #000000 ", 255, 0, 0, 0)] // surrounding whitespace
    public void ColorFromHex_ParsesRgbAndArgbForms(string hex, int a, int r, int g, int b)
    {
        Assert.Equal(new DyncameloColor(a, r, g, b), ColorNodes.FromHex(hex));
    }

    [Theory]
    [InlineData("#12345")] // wrong length
    [InlineData("#GGHHII")] // not hex
    [InlineData("red")]
    public void ColorFromHex_InvalidInput_ThrowsFormatException(string hex)
    {
        var ex = Assert.Throws<FormatException>(() => ColorNodes.FromHex(hex));
        Assert.Contains(hex, ex.Message);
    }

    [Fact]
    public void ColorFromHex_EmptyInput_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => ColorNodes.FromHex(" "));
        Assert.Contains("Color.FromHex", ex.Message);
    }

    [Fact]
    public void ColorComponents_ReturnsChannelsAsInts()
    {
        var components = ColorNodes.Components(new DyncameloColor(128, 10, 20, 30));
        Assert.Equal(10, components["red"]);
        Assert.Equal(20, components["green"]);
        Assert.Equal(30, components["blue"]);
        Assert.Equal(128, components["alpha"]);
    }

    [Fact]
    public void ColorLerp_InterpolatesEachChannel()
    {
        var black = new DyncameloColor(255, 0, 0, 0);
        var white = new DyncameloColor(255, 255, 255, 255);

        Assert.Equal(black, ColorNodes.Lerp(black, white, 0));
        Assert.Equal(white, ColorNodes.Lerp(black, white, 1));
        Assert.Equal(new DyncameloColor(255, 128, 128, 128), ColorNodes.Lerp(black, white, 0.5));
    }

    [Fact]
    public void ColorLerp_ClampsTOutsideZeroToOne()
    {
        var start = new DyncameloColor(255, 100, 100, 100);
        var end = new DyncameloColor(255, 200, 200, 200);
        Assert.Equal(start, ColorNodes.Lerp(start, end, -3));
        Assert.Equal(end, ColorNodes.Lerp(start, end, 42));
    }

    [Fact]
    public void ColorNodes_NullInputs_ThrowHelpfulMessages()
    {
        Assert.Throws<ArgumentNullException>(() => ColorNodes.Components(null!));
        Assert.Throws<ArgumentNullException>(() => ColorNodes.Lerp(null!, new DyncameloColor(255, 0, 0, 0), 0.5));
        Assert.Throws<ArgumentNullException>(() => ColorNodes.Lerp(new DyncameloColor(255, 0, 0, 0), null!, 0.5));
    }

    // -------------------------------------------------------- DateTime (v0.2)

    [Fact]
    public void DateTimeParse_DefaultFormat_AcceptsIsoStyleDates()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.Parse("2026-07-10"));
        Assert.Equal(new DateTime(2026, 7, 10, 14, 30, 0), DateTimeNodes.Parse(" 2026-07-10 14:30 "));
    }

    [Fact]
    public void DateTimeParse_ExactFormat_ControlsInterpretation()
    {
        Assert.Equal(new DateTime(2026, 2, 1), DateTimeNodes.Parse("01/02/2026", "dd/MM/yyyy"));
        Assert.Equal(new DateTime(2026, 1, 2), DateTimeNodes.Parse("01/02/2026", "MM/dd/yyyy"));
    }

    [Fact]
    public void DateTimeParse_InvalidInput_ThrowsClearErrors()
    {
        Assert.Contains("not a date", Assert.Throws<FormatException>(() => DateTimeNodes.Parse("not a date")).Message);
        Assert.Contains("dd/MM/yyyy", Assert.Throws<FormatException>(
            () => DateTimeNodes.Parse("2026-07-10", "dd/MM/yyyy")).Message);
        Assert.Contains("DateTime.Parse", Assert.Throws<ArgumentException>(() => DateTimeNodes.Parse(" ")).Message);
    }

    [Fact]
    public void DateTimeByDate_BuildsMidnightDate()
    {
        Assert.Equal(new DateTime(2026, 7, 10), DateTimeNodes.ByDate(2026, 7, 10));
    }

    [Fact]
    public void DateTimeByDate_InvalidDate_ThrowsWithComponentsInMessage()
    {
        var ex = Assert.Throws<ArgumentException>(() => DateTimeNodes.ByDate(2026, 2, 30));
        Assert.Contains("2026-2-30", ex.Message);
    }

    [Fact]
    public void DateTimeAddDays_SupportsFractionsAndNegatives()
    {
        var start = new DateTime(2026, 7, 10, 12, 0, 0);
        Assert.Equal(new DateTime(2026, 7, 11, 0, 0, 0), DateTimeNodes.AddDays(start, 0.5));
        Assert.Equal(new DateTime(2026, 7, 9, 12, 0, 0), DateTimeNodes.AddDays(start, -1));
    }

    [Fact]
    public void DateTimeDaysBetween_IsSignedAndFractional()
    {
        var start = new DateTime(2026, 7, 10);
        Assert.Equal(2, DateTimeNodes.DaysBetween(start, start.AddDays(2)), 12);
        Assert.Equal(-0.5, DateTimeNodes.DaysBetween(start, start.AddHours(-12)), 12);
    }

    // -------------------------------------------------------- Geometry (v0.2)

    [Fact]
    public void PointDistanceTo_ComputesEuclideanDistance()
    {
        Assert.Equal(5, GeometryNodes.PointDistanceTo(new DyncameloPoint(0, 0, 0), new DyncameloPoint(3, 4, 0)), 12);
        Assert.Equal(0, GeometryNodes.PointDistanceTo(new DyncameloPoint(1, 1, 1), new DyncameloPoint(1, 1, 1)), 12);
    }

    [Fact]
    public void VectorByCoordinates_BuildsVector_DefaultsToZero()
    {
        Assert.Equal(new DyncameloVector(1, 2, 3), GeometryNodes.VectorByCoordinates(1, 2, 3));
        Assert.Equal(new DyncameloVector(0, 0, 0), GeometryNodes.VectorByCoordinates());
        Assert.Equal(5, new DyncameloVector(3, 4, 0).Length, 12);
    }

    [Fact]
    public void BoundingBoxSize_ReturnsExtentsAndCorners()
    {
        var box = new DyncameloBoundingBox(new DyncameloPoint(1, 2, 3), new DyncameloPoint(4, 6, 3));
        var size = GeometryNodes.BoundingBoxSize(box);

        Assert.Equal(3d, size["sizeX"]);
        Assert.Equal(4d, size["sizeY"]);
        Assert.Equal(0d, size["sizeZ"]);
        Assert.Equal(new DyncameloPoint(1, 2, 3), size["min"]);
        Assert.Equal(new DyncameloPoint(4, 6, 3), size["max"]);
    }

    [Fact]
    public void BoundingBoxIntersects_DetectsOverlapTouchAndSeparation()
    {
        var box = new DyncameloBoundingBox(new DyncameloPoint(0, 0, 0), new DyncameloPoint(2, 2, 2));
        var overlapping = new DyncameloBoundingBox(new DyncameloPoint(1, 1, 1), new DyncameloPoint(3, 3, 3));
        var touching = new DyncameloBoundingBox(new DyncameloPoint(2, 0, 0), new DyncameloPoint(4, 2, 2));
        var separate = new DyncameloBoundingBox(new DyncameloPoint(5, 5, 5), new DyncameloPoint(6, 6, 6));

        Assert.True(GeometryNodes.BoundingBoxIntersects(box, overlapping));
        Assert.True(GeometryNodes.BoundingBoxIntersects(overlapping, box));
        Assert.True(GeometryNodes.BoundingBoxIntersects(box, touching));
        Assert.False(GeometryNodes.BoundingBoxIntersects(box, separate));
    }

    [Fact]
    public void NewGeometryNodes_NullInputs_ThrowHelpfulMessages()
    {
        var point = new DyncameloPoint(0, 0, 0);
        var box = new DyncameloBoundingBox(point, new DyncameloPoint(1, 1, 1));

        Assert.Throws<ArgumentNullException>(() => GeometryNodes.PointDistanceTo(null!, point));
        Assert.Throws<ArgumentNullException>(() => GeometryNodes.PointDistanceTo(point, null!));
        Assert.Throws<ArgumentNullException>(() => GeometryNodes.BoundingBoxSize(null!));
        Assert.Throws<ArgumentNullException>(() => GeometryNodes.BoundingBoxIntersects(box, null!));
    }
}
