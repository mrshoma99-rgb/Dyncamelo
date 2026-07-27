using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>BoundingBox.Union — one box fitting boxes, points and [x,y,z] triples.</summary>
public class BoundingBoxUnionTests
{
    [Fact]
    public void Union_OfBoxes_SpansThemAll()
    {
        var a = new DyncameloBoundingBox(new DyncameloPoint(0, 0, 0), new DyncameloPoint(1, 1, 1));
        var b = new DyncameloBoundingBox(new DyncameloPoint(5, -2, 3), new DyncameloPoint(6, 0, 9));

        var union = GeometryNodes.BoundingBoxUnion(new List<object?> { a, b });

        Assert.Equal(0, union.Min.X);
        Assert.Equal(-2, union.Min.Y);
        Assert.Equal(0, union.Min.Z);
        Assert.Equal(6, union.Max.X);
        Assert.Equal(1, union.Max.Y);
        Assert.Equal(9, union.Max.Z);
    }

    [Fact]
    public void Union_MixesPointsTriplesAndNestedLists()
    {
        var union = GeometryNodes.BoundingBoxUnion(new List<object?>
        {
            new DyncameloPoint(1, 1, 1),
            new List<object?> { 4.0, 5, "6" },                       // [x,y,z] triple, mixed numeric forms
            new List<object?>                                        // nested group
            {
                new DyncameloBoundingBox(new DyncameloPoint(-1, 0, 0), new DyncameloPoint(0, 0, 0)),
            },
        });

        Assert.Equal(-1, union.Min.X);
        Assert.Equal(0, union.Min.Y);
        Assert.Equal(0, union.Min.Z);
        Assert.Equal(4, union.Max.X);
        Assert.Equal(5, union.Max.Y);
        Assert.Equal(6, union.Max.Z);
    }

    [Fact]
    public void Union_SinglePoint_IsAZeroSizeBox()
    {
        var union = GeometryNodes.BoundingBoxUnion(new List<object?> { new DyncameloPoint(2, 3, 4) });
        Assert.Equal(union.Min, union.Max);
        Assert.Equal(2, union.Min.X);
    }

    [Fact]
    public void Union_RejectsEmptyAndUnreadableInput()
    {
        Assert.Throws<ArgumentException>(() => GeometryNodes.BoundingBoxUnion(new List<object?>()));
        Assert.Throws<ArgumentException>(() => GeometryNodes.BoundingBoxUnion(new List<object?> { null }));
        Assert.Throws<ArgumentException>(() => GeometryNodes.BoundingBoxUnion(new List<object?> { "not geometry" }));
    }
}
