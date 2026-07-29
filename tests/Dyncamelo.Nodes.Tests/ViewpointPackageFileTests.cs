using System;
using System.Collections.Generic;
using Dyncamelo.Nodes.Portable;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the portable viewpoint package format (Viewpoints.ExportFile /
/// ImportFile): JSON round-trips, the unit-scaling rules (lengths scale, the
/// height field only for orthographic views, raw camera strings are dropped),
/// and the parse-side error contract.
/// </summary>
public class ViewpointPackageFileTests
{
    private static ViewpointPackageFile SamplePackage()
    {
        return new ViewpointPackageFile
        {
            SourceUnits = "Feet",
            SourceDocument = "tower.nwd",
            Views = new List<PortableViewpoint>
            {
                new PortableViewpoint
                {
                    Name = "Level 3 - North",
                    FolderPath = new List<string> { "Reviews", "Week 12" },
                    RawCamera = "{\"native\":\"camera\"}",
                    Position = new[] { 10.0, 20.0, 30.0 },
                    Rotation = new[] { 0.1, 0.2, 0.3, 0.9 },
                    Projection = "Perspective",
                    HeightField = 0.785,
                    AspectRatio = 1.5,
                    FocalDistance = 40.0,
                    WorldUp = new[] { 0.0, 0.0, 1.0 },
                    SectionBox = new PortableSectionBox
                    {
                        Min = new[] { 1.0, 2.0, 3.0 },
                        Max = new[] { 4.0, 5.0, 6.0 },
                        Rotation = new[] { 0.0, 0.0, 0.7071, 0.7071 },
                    },
                },
                new PortableViewpoint
                {
                    Name = "Plan",
                    Projection = "Orthographic",
                    Position = new[] { 0.0, 0.0, 100.0 },
                    HeightField = 50.0,
                },
            },
        };
    }

    // ---------------------------------------------------------------- round-trip

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var parsed = ViewpointPackageFile.Parse(SamplePackage().ToJson());

        Assert.Equal(ViewpointPackageFile.CurrentVersion, parsed.Version);
        Assert.Equal("Feet", parsed.SourceUnits);
        Assert.Equal("tower.nwd", parsed.SourceDocument);
        Assert.Equal(2, parsed.Views.Count);

        var view = parsed.Views[0];
        Assert.Equal("Level 3 - North", view.Name);
        Assert.Equal(new List<string> { "Reviews", "Week 12" }, view.FolderPath);
        Assert.Equal("{\"native\":\"camera\"}", view.RawCamera);
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, view.Position);
        Assert.Equal(new[] { 0.1, 0.2, 0.3, 0.9 }, view.Rotation);
        Assert.Equal("Perspective", view.Projection);
        Assert.False(view.IsOrthographic);
        Assert.Equal(0.785, view.HeightField);
        Assert.Equal(1.5, view.AspectRatio);
        Assert.Equal(40.0, view.FocalDistance);
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, view.WorldUp);
        Assert.NotNull(view.SectionBox);
        Assert.True(view.SectionBox!.Enabled);
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, view.SectionBox.Min);
        Assert.Equal(new[] { 4.0, 5.0, 6.0 }, view.SectionBox.Max);
        Assert.Equal(new[] { 0.0, 0.0, 0.7071, 0.7071 }, view.SectionBox.Rotation);

        var plan = parsed.Views[1];
        Assert.True(plan.IsOrthographic);
        Assert.Null(plan.RawCamera);
        Assert.Null(plan.FocalDistance);
        Assert.Null(plan.WorldUp);
        Assert.Null(plan.SectionBox);
        Assert.Empty(plan.FolderPath);
    }

    // ------------------------------------------------------------------ scaling

    [Fact]
    public void ScaleLengths_ScalesLengths_LeavesAnglesAndRatiosAlone()
    {
        var package = SamplePackage();
        package.ScaleLengths(2.0);

        var view = package.Views[0];
        Assert.Equal(new[] { 20.0, 40.0, 60.0 }, view.Position);
        Assert.Equal(80.0, view.FocalDistance);
        Assert.Equal(new[] { 2.0, 4.0, 6.0 }, view.SectionBox!.Min);
        Assert.Equal(new[] { 8.0, 10.0, 12.0 }, view.SectionBox.Max);

        // Perspective height field is a field-of-view angle — never scaled.
        Assert.Equal(0.785, view.HeightField);
        // Rotations and ratios are unit-free.
        Assert.Equal(new[] { 0.1, 0.2, 0.3, 0.9 }, view.Rotation);
        Assert.Equal(1.5, view.AspectRatio);
        Assert.Equal(new[] { 0.0, 0.0, 0.7071, 0.7071 }, view.SectionBox.Rotation);
    }

    [Fact]
    public void ScaleLengths_ScalesOrthographicHeightField()
    {
        var package = SamplePackage();
        package.ScaleLengths(0.3048);

        Assert.Equal(50.0 * 0.3048, package.Views[1].HeightField, 12);
    }

    [Fact]
    public void ScaleLengths_DropsRawCameras_BecauseTheyEmbedUnscaledCoordinates()
    {
        var package = SamplePackage();
        package.ScaleLengths(2.0);
        Assert.Null(package.Views[0].RawCamera);
    }

    [Fact]
    public void ScaleLengths_ByOne_IsANoOp_KeepingRawCameras()
    {
        var package = SamplePackage();
        package.ScaleLengths(1.0);
        Assert.Equal("{\"native\":\"camera\"}", package.Views[0].RawCamera);
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, package.Views[0].Position);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void ScaleLengths_RejectsNonPositiveFactors(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SamplePackage().ScaleLengths(factor));
    }

    // ------------------------------------------------------------------- parsing

    [Fact]
    public void Parse_RejectsEmptyAndMalformedInput_WithFriendlyMessages()
    {
        Assert.Throws<ArgumentException>(() => ViewpointPackageFile.Parse("  "));

        var malformed = Assert.Throws<InvalidOperationException>(() => ViewpointPackageFile.Parse("{not json"));
        Assert.Contains("Viewpoints.ExportFile", malformed.Message);

        var wrongShape = Assert.Throws<InvalidOperationException>(() => ViewpointPackageFile.Parse("null"));
        Assert.Contains("Viewpoints.ExportFile", wrongShape.Message);
    }

    [Fact]
    public void Parse_RejectsNewerFormatVersions_AndAsksForAnUpdate()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ViewpointPackageFile.Parse("{\"Version\": 99, \"Views\": []}"));
        Assert.Contains("newer Dyncamelo", ex.Message);
    }

    [Fact]
    public void Parse_RejectsViewsWithoutNames()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ViewpointPackageFile.Parse("{\"Version\": 1, \"Views\": [ { \"Position\": [0,0,0] } ]}"));
        Assert.Contains("without a name", ex.Message);
    }

    [Fact]
    public void Parse_ToleratesMissingViewsAndNullEntries()
    {
        Assert.Empty(ViewpointPackageFile.Parse("{\"Version\": 1}").Views);
        Assert.Empty(ViewpointPackageFile.Parse("{\"Version\": 1, \"Views\": null}").Views);
        Assert.Empty(ViewpointPackageFile.Parse("{\"Version\": 1, \"Views\": [null]}").Views);
    }

    [Fact]
    public void Parse_AppliesDefaults_ForOmittedOptionalFields()
    {
        var parsed = ViewpointPackageFile.Parse("{\"Views\": [ { \"Name\": \"A\" } ]}");
        var view = parsed.Views[0];

        Assert.Equal("Perspective", view.Projection);
        Assert.Equal(1.0, view.AspectRatio);
        Assert.Equal(new[] { 0.0, 0.0, 0.0, 1.0 }, view.Rotation);
        Assert.Null(view.SectionBox);
    }
}
