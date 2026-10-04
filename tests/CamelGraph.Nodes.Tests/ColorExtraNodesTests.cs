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
/// ColorExtraNodes: hex output, HSV / HSL conversions, lighten / darken / invert / alpha,
/// the named palettes and the contrast-text picker.
/// </summary>
public class ColorExtraNodesTests
{
    private static CamelGraphColor C(int a, int r, int g, int b) => new CamelGraphColor(a, r, g, b);

    private static string Hex(CamelGraphColor color) => ColorExtraNodes.ToHex(color);

    private static List<string> Hexes(IEnumerable<CamelGraphColor> colors) => colors.Select(Hex).ToList();

    /// <summary>Every colour on a coarse RGB grid (includes both extremes of each channel).</summary>
    private static IEnumerable<CamelGraphColor> Grid(int step = 15)
    {
        for (int r = 0; r <= 255; r += step)
        {
            for (int g = 0; g <= 255; g += step)
            {
                for (int b = 0; b <= 255; b += step)
                {
                    yield return C(255, r, g, b);
                }
            }
        }

        yield return C(255, 255, 255, 255);
    }

    private static void WithCulture(CultureInfo culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>A writable culture that formats numbers the continental-European way ("1,5"), without needing ICU.</summary>
    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NegativeSign = "~";
        return culture;
    }

    // ------------------------------------------------------------ Color.ToHex

    [Fact]
    public void ToHex_FormatsUpperCaseRrggbb_AndDropsAlphaByDefault()
    {
        Assert.Equal("#FF8800", ColorExtraNodes.ToHex(C(255, 255, 136, 0)));
        Assert.Equal("#000000", ColorExtraNodes.ToHex(C(255, 0, 0, 0)));
        Assert.Equal("#ABCDEF", ColorExtraNodes.ToHex(C(10, 0xAB, 0xCD, 0xEF)));
        Assert.Equal("#0A0B0C", ColorExtraNodes.ToHex(C(255, 10, 11, 12)));
    }

    [Fact]
    public void ToHex_IncludeAlpha_PrefixesAaChannel()
    {
        Assert.Equal("#FFFF8800", ColorExtraNodes.ToHex(C(255, 255, 136, 0), includeAlpha: true));
        Assert.Equal("#80123456", ColorExtraNodes.ToHex(C(0x80, 0x12, 0x34, 0x56), includeAlpha: true));
        Assert.Equal("#00FFFFFF", ColorExtraNodes.ToHex(C(0, 255, 255, 255), true));
    }

    [Fact]
    public void ToHex_RoundTripsWithFromHex_InBothForms()
    {
        foreach (var color in Grid(51))
        {
            Assert.Equal(color, ColorNodes.FromHex(ColorExtraNodes.ToHex(color)));
            Assert.Equal(color, ColorNodes.FromHex(ColorExtraNodes.ToHex(color, true)));
        }

        var translucent = C(77, 1, 2, 3);
        Assert.Equal(translucent, ColorNodes.FromHex(ColorExtraNodes.ToHex(translucent, includeAlpha: true)));
        Assert.Equal(C(255, 1, 2, 3), ColorNodes.FromHex(ColorExtraNodes.ToHex(translucent)));
    }

    [Fact]
    public void ToHex_IsCultureIndependent()
    {
        WithCulture(CommaCulture(), () =>
        {
            Assert.Equal("#0A0B0C", ColorExtraNodes.ToHex(C(255, 10, 11, 12)));
            Assert.Equal("#C80A0B0C", ColorExtraNodes.ToHex(C(200, 10, 11, 12), true));
        });
    }

    [Fact]
    public void ToHex_NullColor_NamesTheNodeAndPort()
    {
        var error = Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.ToHex(null!));
        Assert.Contains("Color.ToHex", error.Message);
        Assert.Contains("'color'", error.Message);
    }

    // ----------------------------------------------------------- Color.ByHSV

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(180, 0, 255, 255)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(300, 255, 0, 255)]
    public void ByHsv_PrimariesAndSecondaries(double hue, int r, int g, int b)
    {
        Assert.Equal(C(255, r, g, b), ColorExtraNodes.ByHsv(hue, 1, 1));
    }

    [Fact]
    public void ByHsv_GreysAndExtremes()
    {
        Assert.Equal(C(255, 255, 255, 255), ColorExtraNodes.ByHsv(0, 0, 1));   // white
        Assert.Equal(C(255, 0, 0, 0), ColorExtraNodes.ByHsv(200, 1, 0));       // black, hue irrelevant
        Assert.Equal(C(255, 128, 128, 128), ColorExtraNodes.ByHsv(77, 0, 0.5)); // 127.5 rounds away from zero
        Assert.Equal(C(255, 128, 0, 0), ColorExtraNodes.ByHsv(0, 1, 0.5));
    }

    [Theory]
    [InlineData(360, 0)]
    [InlineData(480, 120)]
    [InlineData(720, 0)]
    [InlineData(-120, 240)]
    [InlineData(-360, 0)]
    [InlineData(-1e-20, 0)]
    public void ByHsv_HueWrapsAround(double hue, double equivalent)
    {
        Assert.Equal(ColorExtraNodes.ByHsv(equivalent, 0.8, 0.9), ColorExtraNodes.ByHsv(hue, 0.8, 0.9));
    }

    [Fact]
    public void ByHsv_SaturationAndValueAreClamped()
    {
        Assert.Equal(ColorExtraNodes.ByHsv(40, 1, 1), ColorExtraNodes.ByHsv(40, 7, 1));
        Assert.Equal(ColorExtraNodes.ByHsv(40, 0, 1), ColorExtraNodes.ByHsv(40, -3, 1));
        Assert.Equal(ColorExtraNodes.ByHsv(40, 1, 1), ColorExtraNodes.ByHsv(40, 1, 9));
        Assert.Equal(C(255, 0, 0, 0), ColorExtraNodes.ByHsv(40, 1, -2));
        Assert.Equal(ColorExtraNodes.ByHsv(40, 1, 1), ColorExtraNodes.ByHsv(40, double.PositiveInfinity, double.PositiveInfinity));
    }

    [Fact]
    public void ByHsv_AlphaDefaultsToOpaque_AndIsClamped()
    {
        Assert.Equal(255, ColorExtraNodes.ByHsv(10, 1, 1).A);
        Assert.Equal(128, ColorExtraNodes.ByHsv(10, 1, 1, 128).A);
        Assert.Equal(255, ColorExtraNodes.ByHsv(10, 1, 1, 999).A);
        Assert.Equal(0, ColorExtraNodes.ByHsv(10, 1, 1, -4).A);
    }

    [Fact]
    public void ByHsv_RejectsNonFiniteHue_AndNaNChannels()
    {
        Assert.Contains("hue", Assert.Throws<ArgumentException>(() => ColorExtraNodes.ByHsv(double.NaN, 1, 1)).Message);
        Assert.Throws<ArgumentException>(() => ColorExtraNodes.ByHsv(double.PositiveInfinity, 1, 1));
        Assert.Contains("saturation", Assert.Throws<ArgumentException>(() => ColorExtraNodes.ByHsv(0, double.NaN, 1)).Message);
        Assert.Contains("value", Assert.Throws<ArgumentException>(() => ColorExtraNodes.ByHsv(0, 1, double.NaN)).Message);
    }

    // ----------------------------------------------------------- Color.ToHSV

    [Theory]
    [InlineData(255, 0, 0, 0, 1, 1)]
    [InlineData(0, 255, 0, 120, 1, 1)]
    [InlineData(0, 0, 255, 240, 1, 1)]
    [InlineData(255, 255, 0, 60, 1, 1)]
    [InlineData(0, 255, 255, 180, 1, 1)]
    [InlineData(255, 0, 255, 300, 1, 1)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(255, 255, 255, 0, 0, 1)]
    public void ToHsv_KnownColors(int r, int g, int b, double hue, double saturation, double value)
    {
        var parts = ColorExtraNodes.ToHsv(C(255, r, g, b));

        Assert.Equal(new[] { "hue", "saturation", "value" }, parts.Keys.ToArray());
        Assert.Equal(hue, (double)parts["hue"], 9);
        Assert.Equal(saturation, (double)parts["saturation"], 9);
        Assert.Equal(value, (double)parts["value"], 9);
    }

    [Fact]
    public void ToHsv_OrangeAndGrey()
    {
        var orange = ColorExtraNodes.ToHsv(C(255, 255, 128, 0));
        Assert.Equal(128d / 255d * 60d, (double)orange["hue"], 9);
        Assert.Equal(1d, (double)orange["saturation"], 9);

        var grey = ColorExtraNodes.ToHsv(C(255, 128, 128, 128));
        Assert.Equal(0d, (double)grey["hue"]);
        Assert.Equal(0d, (double)grey["saturation"]);
        Assert.Equal(128d / 255d, (double)grey["value"], 9);
    }

    [Fact]
    public void ToHsv_HueStaysBelow360_ForReddishMagentas()
    {
        var hue = (double)ColorExtraNodes.ToHsv(C(255, 255, 0, 1))["hue"];
        Assert.InRange(hue, 359, 360);
        Assert.True(hue < 360d);
    }

    [Fact]
    public void ToHsv_ThenByHsv_RoundTripsEveryGridColor_KeepingAlpha()
    {
        foreach (var color in Grid(5))
        {
            var parts = ColorExtraNodes.ToHsv(color);
            var back = ColorExtraNodes.ByHsv((double)parts["hue"], (double)parts["saturation"], (double)parts["value"], color.A);
            Assert.Equal(color, back);
        }

        var translucent = C(40, 200, 100, 50);
        var hsv = ColorExtraNodes.ToHsv(translucent);
        Assert.Equal(translucent, ColorExtraNodes.ByHsv((double)hsv["hue"], (double)hsv["saturation"], (double)hsv["value"], 40));
    }

    [Fact]
    public void ToHsv_NullColor_Throws()
    {
        Assert.Contains("Color.ToHSV", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.ToHsv(null!)).Message);
    }

    // ------------------------------------------------- Color.Lighten / Darken

    [Fact]
    public void Lighten_ShiftsHslLightnessUp()
    {
        // Pure red is HSL (0, 1, 0.5); +0.2 lightness gives (0, 1, 0.7) = #FF6666.
        Assert.Equal(C(255, 255, 102, 102), ColorExtraNodes.Lighten(C(255, 255, 0, 0), 0.2));
        Assert.Equal(C(255, 128, 128, 128), ColorExtraNodes.Lighten(C(255, 0, 0, 0), 0.5));
    }

    [Fact]
    public void Darken_ShiftsHslLightnessDown()
    {
        // (0, 1, 0.5) - 0.2 = (0, 1, 0.3) = #990000.
        Assert.Equal(C(255, 153, 0, 0), ColorExtraNodes.Darken(C(255, 255, 0, 0), 0.2));
        Assert.Equal(C(255, 0, 0, 0), ColorExtraNodes.Darken(C(255, 255, 0, 0), 0.5));
    }

    [Fact]
    public void LightenAndDarken_DefaultAmountIsTwentyPercent()
    {
        var color = C(255, 30, 120, 200);
        Assert.Equal(ColorExtraNodes.Lighten(color, 0.2), ColorExtraNodes.Lighten(color));
        Assert.Equal(ColorExtraNodes.Darken(color, 0.2), ColorExtraNodes.Darken(color));
    }

    [Fact]
    public void LightenAndDarken_ByZeroLeaveEveryGridColorUnchanged()
    {
        foreach (var color in Grid(5))
        {
            Assert.Equal(color, ColorExtraNodes.Lighten(color, 0));
            Assert.Equal(color, ColorExtraNodes.Darken(color, 0));
        }
    }

    [Fact]
    public void Lighten_FullAmountIsWhite_Darken_FullAmountIsBlack()
    {
        foreach (var color in Grid(51))
        {
            Assert.Equal(C(255, 255, 255, 255), ColorExtraNodes.Lighten(color, 1));
            Assert.Equal(C(255, 0, 0, 0), ColorExtraNodes.Darken(color, 1));
        }
    }

    [Fact]
    public void LightenAndDarken_ClampTheAmount()
    {
        var color = C(255, 90, 150, 30);
        Assert.Equal(ColorExtraNodes.Lighten(color, 1), ColorExtraNodes.Lighten(color, 40));
        Assert.Equal(ColorExtraNodes.Lighten(color, 0), ColorExtraNodes.Lighten(color, -0.5));
        Assert.Equal(ColorExtraNodes.Darken(color, 1), ColorExtraNodes.Darken(color, 40));
        Assert.Equal(ColorExtraNodes.Darken(color, 0), ColorExtraNodes.Darken(color, -0.5));
    }

    [Fact]
    public void LightenAndDarken_KeepAlpha_AndHue()
    {
        var color = C(100, 200, 50, 50);
        var lighter = ColorExtraNodes.Lighten(color, 0.1);
        var darker = ColorExtraNodes.Darken(color, 0.1);

        Assert.Equal(100, lighter.A);
        Assert.Equal(100, darker.A);

        var hue = (double)ColorExtraNodes.ToHsv(color)["hue"];
        Assert.Equal(hue, (double)ColorExtraNodes.ToHsv(lighter)["hue"], 0);
        Assert.Equal(hue, (double)ColorExtraNodes.ToHsv(darker)["hue"], 0);
    }

    [Fact]
    public void Lighten_GetsBrighter_Darken_GetsDarker()
    {
        var color = C(255, 70, 110, 160);
        var lighter = ColorExtraNodes.Lighten(color, 0.15);
        var darker = ColorExtraNodes.Darken(color, 0.15);

        Assert.True(lighter.R > color.R && lighter.G > color.G && lighter.B > color.B);
        Assert.True(darker.R < color.R && darker.G < color.G && darker.B < color.B);
    }

    [Fact]
    public void LightenAndDarken_GreysStayGrey()
    {
        var lighter = ColorExtraNodes.Lighten(C(255, 100, 100, 100), 0.1);
        Assert.Equal(lighter.R, lighter.G);
        Assert.Equal(lighter.G, lighter.B);
        Assert.True(lighter.R > 100);
        Assert.Equal(C(255, 204, 204, 204), ColorExtraNodes.Darken(C(255, 255, 255, 255), 0.2));
    }

    [Fact]
    public void LightenAndDarken_RejectNullAndNaN()
    {
        Assert.Contains("Color.Lighten", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.Lighten(null!)).Message);
        Assert.Contains("Color.Darken", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.Darken(null!)).Message);
        Assert.Contains("amount", Assert.Throws<ArgumentException>(() => ColorExtraNodes.Lighten(C(255, 1, 2, 3), double.NaN)).Message);
        Assert.Contains("amount", Assert.Throws<ArgumentException>(() => ColorExtraNodes.Darken(C(255, 1, 2, 3), double.NaN)).Message);
    }

    // ------------------------------------------------------- Color.WithAlpha

    [Fact]
    public void WithAlpha_ReplacesOnlyTheAlphaChannel()
    {
        var result = ColorExtraNodes.WithAlpha(C(255, 10, 20, 30), 64);
        Assert.Equal(C(64, 10, 20, 30), result);
    }

    [Theory]
    [InlineData(300, 255)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(255, 255)]
    public void WithAlpha_ClampsToByteRange(int alpha, int expected)
    {
        Assert.Equal(expected, ColorExtraNodes.WithAlpha(C(100, 1, 2, 3), alpha).A);
    }

    [Fact]
    public void WithAlpha_NullColor_Throws()
    {
        Assert.Contains("Color.WithAlpha", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.WithAlpha(null!, 10)).Message);
    }

    // --------------------------------------------------------- Color.Invert

    [Fact]
    public void Invert_FlipsRgb_KeepsAlpha()
    {
        Assert.Equal(C(77, 245, 235, 225), ColorExtraNodes.Invert(C(77, 10, 20, 30)));
        Assert.Equal(C(255, 255, 255, 255), ColorExtraNodes.Invert(C(255, 0, 0, 0)));
        Assert.Equal(C(255, 0, 255, 255), ColorExtraNodes.Invert(C(255, 255, 0, 0)));
    }

    [Fact]
    public void Invert_Twice_IsTheOriginal()
    {
        foreach (var color in Grid(51))
        {
            Assert.Equal(color, ColorExtraNodes.Invert(ColorExtraNodes.Invert(color)));
        }
    }

    [Fact]
    public void Invert_NullColor_Throws()
    {
        Assert.Contains("Color.Invert", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.Invert(null!)).Message);
    }

    // -------------------------------------------------------- Color.Palette

    [Fact]
    public void Palette_Default_IsTheEightOkabeItoColors()
    {
        var colors = ColorExtraNodes.Palette();

        Assert.Equal(
            new[] { "#000000", "#E69F00", "#56B4E9", "#009E73", "#F0E442", "#0072B2", "#D55E00", "#CC79A7" },
            Hexes(colors));
        Assert.Equal(8, colors.Distinct().Count());
        Assert.All(colors, c => Assert.Equal(255, c.A));
    }

    [Fact]
    public void Palette_Tableau_HasTheTenTableauColors()
    {
        var colors = ColorExtraNodes.Palette("tableau", 10);

        Assert.Equal(
            new[] { "#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F", "#EDC948", "#B07AA1", "#FF9DA7", "#9C755F", "#BAB0AC" },
            Hexes(colors));
    }

    [Fact]
    public void Palette_Pastel_IsLight()
    {
        var colors = ColorExtraNodes.Palette("pastel", 9);

        Assert.Equal(9, colors.Distinct().Count());
        Assert.All(colors, c => Assert.True(c.R >= 0xB0 && c.G >= 0xB0 && c.B >= 0xA0, Hex(c) + " is not a pastel"));
    }

    [Fact]
    public void Palette_Status_IsGoodWarnBadUnknown()
    {
        var colors = ColorExtraNodes.Palette("status", 4);

        Assert.Equal(4, colors.Count);
        var good = colors[0]; var warn = colors[1]; var bad = colors[2]; var unknown = colors[3];
        Assert.True(good.G > good.R && good.G > good.B, "good should be green");
        Assert.True(warn.R > 200 && warn.G > 120 && warn.B < 60, "warn should be amber");
        Assert.True(bad.R > bad.G && bad.R > bad.B, "bad should be red");
        Assert.True(Math.Abs(unknown.R - unknown.G) < 12 && Math.Abs(unknown.G - unknown.B) < 12, "unknown should be grey");
    }

    [Theory]
    [InlineData("colourblind", 8)]
    [InlineData("tableau", 10)]
    [InlineData("pastel", 9)]
    [InlineData("status", 4)]
    public void Palette_FixedPalettes_CycleBeyondTheirSize(string name, int size)
    {
        var baseColors = ColorExtraNodes.Palette(name, size);
        var cycled = ColorExtraNodes.Palette(name, size * 2 + 3);

        Assert.Equal(size * 2 + 3, cycled.Count);
        for (int i = 0; i < cycled.Count; i++)
        {
            Assert.Equal(baseColors[i % size], cycled[i]);
        }
    }

    [Theory]
    [InlineData("colourblind")]
    [InlineData("tableau")]
    [InlineData("status")]
    public void Palette_FewerColorsThanThePalette_ReturnsTheFirstOnes(string name)
    {
        var all = ColorExtraNodes.Palette(name, 4);
        var first = ColorExtraNodes.Palette(name, 2);

        Assert.Equal(all.Take(2), first);
        Assert.Single(ColorExtraNodes.Palette(name, 1));
    }

    [Fact]
    public void Palette_Viridis_AtNineColorsIsTheAnchors_AndEndsAreFixed()
    {
        Assert.Equal(
            new[] { "#440154", "#472D7B", "#3B528B", "#2C728E", "#21908C", "#27AD81", "#5CC863", "#AADC32", "#FDE725" },
            Hexes(ColorExtraNodes.Palette("viridis", 9)));

        Assert.Equal(new[] { "#440154", "#FDE725" }, Hexes(ColorExtraNodes.Palette("viridis", 2)));
        Assert.Equal(new[] { "#440154" }, Hexes(ColorExtraNodes.Palette("viridis", 1)));
        Assert.Equal(
            new[] { "#440154", "#3B528B", "#21908C", "#5CC863", "#FDE725" },
            Hexes(ColorExtraNodes.Palette("viridis", 5)));
    }

    [Fact]
    public void Palette_Viridis_Interpolates_AnyCount_Smoothly()
    {
        var colors = ColorExtraNodes.Palette("viridis", 100);

        Assert.Equal(100, colors.Count);
        Assert.Equal("#440154", Hex(colors[0]));
        Assert.Equal("#FDE725", Hex(colors[99]));
        // Viridis' green channel only ever increases from end to end.
        for (int i = 1; i < colors.Count; i++)
        {
            Assert.True(colors[i].G >= colors[i - 1].G, "green dips at " + i);
        }
    }

    [Fact]
    public void Palette_Heat_RunsFromPaleYellowToDarkRed()
    {
        var colors = ColorExtraNodes.Palette("heat", 7);

        Assert.Equal("#FFFFCC", Hex(colors[0]));
        Assert.Equal("#800026", Hex(colors[6]));
        // Red and green only ever fall as the ramp heats up (yellow -> orange -> dark red).
        for (int i = 1; i < colors.Count; i++)
        {
            Assert.True(colors[i].R <= colors[i - 1].R && colors[i].G <= colors[i - 1].G, "red or green rises at " + i);
        }
    }

    [Fact]
    public void Palette_Grey_RunsFromWhiteToBlack_InEvenSteps()
    {
        Assert.Equal(new[] { "#FFFFFF", "#808080", "#000000" }, Hexes(ColorExtraNodes.Palette("grey", 3)));
        Assert.Equal(
            new[] { "#FFFFFF", "#BFBFBF", "#808080", "#404040", "#000000" },
            Hexes(ColorExtraNodes.Palette("grey", 5)));

        var all = ColorExtraNodes.Palette("grey", 256);
        for (int i = 0; i < all.Count; i++)
        {
            Assert.Equal(255 - i, all[i].R);
            Assert.Equal(all[i].R, all[i].G);
            Assert.Equal(all[i].R, all[i].B);
        }
    }

    [Theory]
    [InlineData("Viridis")]
    [InlineData("VIRIDIS")]
    [InlineData("  viridis  ")]
    public void Palette_NamesAreCaseInsensitive_AndTrimmed(string name)
    {
        Assert.Equal(ColorExtraNodes.Palette("viridis", 6), ColorExtraNodes.Palette(name, 6));
    }

    [Fact]
    public void Palette_AcceptsAmericanSpellingAliases()
    {
        Assert.Equal(ColorExtraNodes.Palette("colourblind", 8), ColorExtraNodes.Palette("Colorblind", 8));
        Assert.Equal(ColorExtraNodes.Palette("grey", 8), ColorExtraNodes.Palette("gray", 8));
    }

    [Fact]
    public void Palette_NameLookup_IgnoresTheCurrentCulture()
    {
        WithCulture(CommaCulture(), () =>
        {
            Assert.Equal(5, ColorExtraNodes.Palette("VIRIDIS", 5).Count);
            Assert.Equal(5, ColorExtraNodes.Palette("TABLEAU", 5).Count);
        });
    }

    [Theory]
    [InlineData("rainbow")]
    [InlineData("colourblind2")]
    public void Palette_UnknownName_ListsTheValidNames(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => ColorExtraNodes.Palette(name, 4));

        Assert.Contains("'" + name + "'", error.Message);
        Assert.Contains("colourblind, tableau, pastel, status, viridis, heat, grey", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Palette_MissingName_IsAnErrorListingTheValidNames(string? name)
    {
        var error = Assert.Throws<ArgumentException>(() => ColorExtraNodes.Palette(name!, 4));

        Assert.Contains("palette name", error.Message);
        Assert.Contains("viridis", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(257)]
    public void Palette_CountOutsideOneTo256_Throws(int count)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => ColorExtraNodes.Palette("viridis", count));

        Assert.Contains("between 1 and 256", error.Message);
        Assert.Contains(count.ToString(CultureInfo.InvariantCulture), error.Message);
    }

    [Fact]
    public void Palette_ReturnsAFreshListEachCall()
    {
        var first = ColorExtraNodes.Palette("tableau", 5);
        first.Clear();

        Assert.Equal(5, ColorExtraNodes.Palette("tableau", 5).Count);
    }

    [Fact]
    public void Palette_EveryRampColorIsOpaque()
    {
        foreach (var name in new[] { "viridis", "heat", "grey" })
        {
            Assert.All(ColorExtraNodes.Palette(name, 33), c => Assert.Equal(255, c.A));
        }
    }

    // ------------------------------------------------- Color.ContrastText

    [Fact]
    public void ContrastText_BlackOnLight_WhiteOnDark()
    {
        var black = C(255, 0, 0, 0);
        var white = C(255, 255, 255, 255);

        Assert.Equal(black, ColorExtraNodes.ContrastText(C(255, 255, 255, 255)));
        Assert.Equal(white, ColorExtraNodes.ContrastText(C(255, 0, 0, 0)));
        Assert.Equal(black, ColorExtraNodes.ContrastText(C(255, 255, 255, 0)));    // yellow
        Assert.Equal(white, ColorExtraNodes.ContrastText(C(255, 0, 0, 128)));      // navy
        Assert.Equal(black, ColorExtraNodes.ContrastText(C(255, 255, 0, 0)));      // red: 5.25 vs 4.0
        Assert.Equal(white, ColorExtraNodes.ContrastText(C(255, 0, 0, 255)));      // blue: 2.4 vs 8.6
        Assert.Equal(black, ColorExtraNodes.ContrastText(C(255, 0, 255, 0)));      // green
    }

    [Fact]
    public void ContrastText_SwitchesAtTheWcagCrossover()
    {
        // The two contrast ratios are equal at relative luminance ~0.179, which lies between #757575 and #767676.
        Assert.Equal(C(255, 255, 255, 255), ColorExtraNodes.ContrastText(C(255, 0x75, 0x75, 0x75)));
        Assert.Equal(C(255, 0, 0, 0), ColorExtraNodes.ContrastText(C(255, 0x76, 0x76, 0x76)));
    }

    [Fact]
    public void ContrastText_IgnoresTheBackgroundAlpha_AndIsAlwaysOpaque()
    {
        Assert.Equal(ColorExtraNodes.ContrastText(C(255, 20, 30, 40)), ColorExtraNodes.ContrastText(C(0, 20, 30, 40)));
        Assert.Equal(255, ColorExtraNodes.ContrastText(C(0, 250, 250, 250)).A);
    }

    [Fact]
    public void ContrastText_NeverReadsWorseThanWcagAaLargeTextOnAnyColor()
    {
        foreach (var color in Grid(15))
        {
            var text = ColorExtraNodes.ContrastText(color);
            var ratio = ContrastRatio(color, text);
            // The best of black / white is at least ~4.58:1 for every possible background.
            Assert.True(ratio >= 4.5, Hex(color) + " gives only " + ratio);
        }
    }

    [Fact]
    public void ContrastText_Null_Throws()
    {
        Assert.Contains("Color.ContrastText", Assert.Throws<ArgumentNullException>(() => ColorExtraNodes.ContrastText(null!)).Message);
    }

    private static double ContrastRatio(CamelGraphColor a, CamelGraphColor b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(CamelGraphColor c)
    {
        static double Lin(byte v)
        {
            var s = v / 255d;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
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
    public void Registration_AllNodesAreImported_UniquelyNamed_InTheColorCategory()
    {
        var registry = CreateRegistry();
        var names = new[]
        {
            "Color.ToHex", "Color.ByHSV", "Color.ToHSV", "Color.Lighten", "Color.Darken",
            "Color.WithAlpha", "Color.Invert", "Color.Palette", "Color.ContrastText",
        };

        foreach (var name in names)
        {
            var definition = Definition(registry, name); // Single: the name is unique in the whole library
            Assert.Equal("Color", definition.Category);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description), name + " has no description");
            Assert.NotEmpty(definition.SearchTags);
        }
    }

    [Fact]
    public void Registration_ToHsvIsAnInfoNode_WithThreeNumberPorts()
    {
        var definition = Definition(CreateRegistry(), "Color.ToHSV");

        Assert.Equal(NodeFunction.Info, definition.Function);
        Assert.Equal(new[] { "hue", "saturation", "value" }, definition.MultiReturnKeys);
        Assert.Equal(new[] { "hue", "saturation", "value" }, definition.Outputs.Select(o => o.Name));
        Assert.All(definition.Outputs, o => Assert.Equal("number", o.Kind));
    }

    [Fact]
    public void Registration_PaletteExposesTheDropdownAndRange()
    {
        var definition = Definition(CreateRegistry(), "Color.Palette");

        var name = definition.Inputs.Single(i => i.Name == "name");
        Assert.Equal(new[] { "colourblind", "tableau", "pastel", "status", "viridis", "heat", "grey" }, name.Choices);
        Assert.True(name.HasDefault);
        Assert.Equal("colourblind", name.DefaultValue);

        var count = definition.Inputs.Single(i => i.Name == "count");
        Assert.True(count.HasDefault);
        Assert.Equal(8, count.DefaultValue);
        Assert.NotNull(count.Range);
        Assert.Equal(1d, count.Range!.Min);
        Assert.Equal(256d, count.Range.Max);
    }

    [Fact]
    public void Registration_ColorNodesAreCreateNodes()
    {
        var registry = CreateRegistry();
        foreach (var name in new[] { "Color.ToHex", "Color.ByHSV", "Color.Lighten", "Color.Darken", "Color.WithAlpha", "Color.Invert", "Color.Palette", "Color.ContrastText" })
        {
            Assert.Equal(NodeFunction.Create, Definition(registry, name).Function);
        }
    }

    [Fact]
    public void Engine_FromHexLightenToHex_ChainRuns()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var hex = new StringInputNode { Value = "#FF0000" };
        var amount = new NumberInputNode { Value = 0.2 };
        var parse = new ZeroTouchNodeModel(Definition(registry, "Color.FromHex"));
        var lighten = new ZeroTouchNodeModel(Definition(registry, "Color.Lighten"));
        var format = new ZeroTouchNodeModel(Definition(registry, "Color.ToHex"));
        foreach (var node in new NodeModel[] { hex, amount, parse, lighten, format })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.Connect(hex.OutPorts[0], parse.InPorts[0]).Success);
        Assert.True(graph.Connect(parse.OutPorts[0], lighten.InPorts[0]).Success);
        Assert.True(graph.Connect(amount.OutPorts[0], lighten.InPorts[1]).Success);
        Assert.True(graph.Connect(lighten.OutPorts[0], format.InPorts[0]).Success);

        var result = new GraphEngine().Run(graph);

        Assert.True(result.Success);
        Assert.Equal(NodeState.Executed, format.State);
        Assert.Equal("#FF6666", format.OutPorts[0].Value);
    }

    [Fact]
    public void Engine_PaletteWithUnconnectedInputs_UsesTheDefaults()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var palette = new ZeroTouchNodeModel(Definition(registry, "Color.Palette"));
        graph.AddNode(palette);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, palette.State);
        var colors = Assert.IsAssignableFrom<IEnumerable<CamelGraphColor>>(palette.OutPorts[0].Value).ToList();
        Assert.Equal(ColorExtraNodes.Palette("colourblind", 8), colors);
    }

    [Fact]
    public void Engine_UnknownPaletteName_BecomesAnErrorNodeWithTheValidNames()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var name = new StringInputNode { Value = "neon" };
        var palette = new ZeroTouchNodeModel(Definition(registry, "Color.Palette"));
        graph.AddNode(name);
        graph.AddNode(palette);
        Assert.True(graph.Connect(name.OutPorts[0], palette.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Error, palette.State);
        Assert.Contains("viridis", palette.StateMessage);
    }

    [Fact]
    public void Engine_ToHsvSplitsIntoThreePorts()
    {
        var registry = CreateRegistry();
        var graph = new GraphModel();
        var hex = new StringInputNode { Value = "#00FF00" };
        var parse = new ZeroTouchNodeModel(Definition(registry, "Color.FromHex"));
        var hsv = new ZeroTouchNodeModel(Definition(registry, "Color.ToHSV"));
        graph.AddNode(hex);
        graph.AddNode(parse);
        graph.AddNode(hsv);
        Assert.True(graph.Connect(hex.OutPorts[0], parse.InPorts[0]).Success);
        Assert.True(graph.Connect(parse.OutPorts[0], hsv.InPorts[0]).Success);

        new GraphEngine().Run(graph);

        Assert.Equal(NodeState.Executed, hsv.State);
        Assert.Equal(120d, (double)hsv.OutPorts[0].Value!, 9);
        Assert.Equal(1d, (double)hsv.OutPorts[1].Value!, 9);
        Assert.Equal(1d, (double)hsv.OutPorts[2].Value!, 9);
    }
}
