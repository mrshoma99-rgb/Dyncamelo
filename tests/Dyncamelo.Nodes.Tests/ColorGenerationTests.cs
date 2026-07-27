using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Color generation nodes: seeded random colors, distinct random lists,
/// two-color gradients, and the value → color categorical mapping.
/// </summary>
public class ColorGenerationTests
{
    // ---------------------------------------------------------- Color.Random

    [Fact]
    public void Random_IsStablePerSeed_AndSeedsDiffer()
    {
        Assert.Equal(ColorNodes.Random(7), ColorNodes.Random(7));
        Assert.NotEqual(ColorNodes.Random(1), ColorNodes.Random(2));
        Assert.Equal(255, (int)ColorNodes.Random(3).A);
    }

    // ------------------------------------------------------ Color.RandomList

    [Fact]
    public void RandomList_CountRespected_AllDistinct_StablePerSeed()
    {
        var colors = ColorNodes.RandomList(12, seed: 5);
        Assert.Equal(12, colors.Count);
        Assert.Equal(12, colors.Distinct().Count());
        Assert.Equal(colors, ColorNodes.RandomList(12, seed: 5));
        Assert.NotEqual(colors[0], ColorNodes.RandomList(12, seed: 6)[0]);
    }

    [Fact]
    public void RandomList_RejectsNonPositiveCount()
    {
        Assert.Throws<ArgumentException>(() => ColorNodes.RandomList(0));
    }

    // -------------------------------------------------------- Color.Gradient

    [Fact]
    public void Gradient_IncludesEndpoints_AndBlendsBetween()
    {
        var start = new DyncameloColor(255, 0, 0, 0);
        var end = new DyncameloColor(255, 200, 100, 50);
        var colors = ColorNodes.Gradient(5, start, end);

        Assert.Equal(5, colors.Count);
        Assert.Equal(start, colors[0]);
        Assert.Equal(end, colors[4]);
        Assert.Equal(new DyncameloColor(255, 100, 50, 25), colors[2]); // midpoint
    }

    [Fact]
    public void Gradient_SingleColor_IsTheStart_AndDefaultsExist()
    {
        var start = new DyncameloColor(255, 10, 20, 30);
        Assert.Equal(start, Assert.Single(ColorNodes.Gradient(1, start)));

        var defaults = ColorNodes.Gradient(3);
        Assert.Equal(3, defaults.Count);
        Assert.NotEqual(defaults[0], defaults[2]);
        Assert.Throws<ArgumentException>(() => ColorNodes.Gradient(0));
    }

    // -------------------------------------------------------- Color.ByValues

    [Fact]
    public void ByValues_EqualValuesShareColors_LegendAligned()
    {
        var values = new List<object?> { "A", "B", "A", 2, "B" };
        var mapping = ColorNodes.ByValues(values);

        var colors = (List<DyncameloColor>)mapping["colors"]!;
        var uniqueValues = (List<object?>)mapping["uniqueValues"]!;
        var uniqueColors = (List<DyncameloColor>)mapping["uniqueColors"]!;

        Assert.Equal(5, colors.Count);
        Assert.Equal(colors[0], colors[2]);
        Assert.Equal(colors[1], colors[4]);
        Assert.NotEqual(colors[0], colors[1]);
        Assert.NotEqual(colors[0], colors[3]);

        Assert.Equal(new object?[] { "A", "B", 2 }, uniqueValues);
        Assert.Equal(colors[0], uniqueColors[0]);
        Assert.Equal(colors[1], uniqueColors[1]);
        Assert.Equal(colors[3], uniqueColors[2]);
    }

    [Fact]
    public void ByValues_CustomPalette_UsedInOrderAndCycled()
    {
        var red = new DyncameloColor(255, 255, 0, 0);
        var values = new List<object?> { "x", "y", "z" };
        var palette = new List<object?> { red, "#00FF00" };

        var mapping = ColorNodes.ByValues(values, palette);
        var uniqueColors = (List<DyncameloColor>)mapping["uniqueColors"]!;

        Assert.Equal(red, uniqueColors[0]);
        Assert.Equal(new DyncameloColor(255, 0, 255, 0), uniqueColors[1]);
        Assert.Equal(red, uniqueColors[2]); // cycled
    }

    [Fact]
    public void ByValues_RejectsEmptyValues_AndUnreadablePaletteEntries()
    {
        Assert.Throws<ArgumentException>(() => ColorNodes.ByValues(new List<object?>()));
        Assert.Throws<ArgumentException>(() =>
            ColorNodes.ByValues(new List<object?> { "a" }, new List<object?> { 42 }));
    }
}
