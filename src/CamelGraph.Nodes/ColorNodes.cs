using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// Color construction nodes. Colors are <see cref="CamelGraphColor"/> values;
/// host layers (UI, Navisworks appearance overrides) convert them to their
/// native color types.
/// </summary>
[NodeCategory("Color")]
public static class ColorNodes
{
    // One million colours is already 16 MB of references; more than that is a wrong number, not a wish.
    private const int MaxColors = 1_000_000;

    /// <summary>
    /// Builds a color from alpha, red, green and blue channels. Channel values
    /// outside 0–255 are clamped.
    /// </summary>
    /// <param name="a">Alpha channel, 0–255 (default fully opaque).</param>
    /// <param name="r">Red channel, 0–255.</param>
    /// <param name="g">Green channel, 0–255.</param>
    /// <param name="b">Blue channel, 0–255.</param>
    /// <returns>The color.</returns>
    [NodeName("Color.ByARGB")]
    [return: NodeName("color")]
    [NodeDescription("Creates a color from alpha, red, green and blue values (0-255).")]
    [NodeSearchTags("rgb", "argb", "rgba")]
    public static CamelGraphColor ByArgb(
        [NodeRange(0, 255)] int a = 255,
        [NodeRange(0, 255)] int r = 0,
        [NodeRange(0, 255)] int g = 0,
        [NodeRange(0, 255)] int b = 0)
    {
        return new CamelGraphColor(a, r, g, b);
    }

    /// <summary>
    /// Parses a hex color string: "#RRGGBB" or "#AARRGGBB" (the leading "#" is
    /// optional). Without an alpha component the color is fully opaque.
    /// </summary>
    /// <param name="hex">The hex string, e.g. "#FF8800".</param>
    /// <returns>The parsed color.</returns>
    [NodeName("Color.FromHex")]
    [return: NodeName("color")]
    [NodeDescription("Parses a hex color string (\"#RRGGBB\" or \"#AARRGGBB\").")]
    [NodeSearchTags("hex", "html", "web", "parse")]
    public static CamelGraphColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("Color.FromHex requires a hex string such as \"#RRGGBB\" or \"#AARRGGBB\".", nameof(hex));
        }

        var digits = hex.Trim();
        if (digits.StartsWith("#", StringComparison.Ordinal))
        {
            digits = digits.Substring(1);
        }

        if ((digits.Length != 6 && digits.Length != 8) ||
            !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            throw new FormatException(
                "Color.FromHex cannot parse '" + hex + "'. Expected \"#RRGGBB\" or \"#AARRGGBB\" (the \"#\" is optional).");
        }

        var alpha = digits.Length == 8 ? (int)((value >> 24) & 0xFF) : 255;
        return new CamelGraphColor(
            alpha,
            (int)((value >> 16) & 0xFF),
            (int)((value >> 8) & 0xFF),
            (int)(value & 0xFF));
    }

    /// <summary>Decomposes a color into its red, green, blue and alpha channels.</summary>
    /// <param name="color">The color to decompose.</param>
    /// <returns>Dictionary with "red", "green", "blue" and "alpha" values (0-255).</returns>
    [NodeName("Color.Components")]
    [MultiReturn("red", "green", "blue", "alpha")]
    [PortKinds("integer", "integer", "integer", "integer")]
    [NodeDescription("Splits a color into its red, green, blue and alpha channels (0-255).")]
    [NodeSearchTags("deconstruct", "channels", "rgb", "argb")]
    public static Dictionary<string, object> Components(CamelGraphColor color)
    {
        if (color == null)
        {
            throw new ArgumentNullException(nameof(color), "Color.Components requires a color.");
        }

        return new Dictionary<string, object>
        {
            ["red"] = (int)color.R,
            ["green"] = (int)color.G,
            ["blue"] = (int)color.B,
            ["alpha"] = (int)color.A,
        };
    }

    /// <summary>
    /// Linearly interpolates between two colors. t = 0 yields the start color,
    /// t = 1 the end color; values outside 0–1 are clamped. Combine with
    /// Math.MapRange to build value-driven gradients.
    /// </summary>
    /// <param name="start">Color at t = 0.</param>
    /// <param name="end">Color at t = 1.</param>
    /// <param name="t">Interpolation parameter (clamped to 0–1; NaN counts as 0, with a warning).</param>
    /// <returns>The interpolated color.</returns>
    [NodeName("Color.Lerp")]
    [return: NodeName("color")]
    [NodeDescription("Interpolates between two colors (t clamped to 0-1). A t that is not a number (NaN) gives the start color and a warning.")]
    [NodeSearchTags("interpolate", "blend", "gradient", "mix")]
    public static CamelGraphColor Lerp(CamelGraphColor start, CamelGraphColor end, [NodeRange(0, 1, Step = 0.05)] double t)
    {
        if (start == null)
        {
            throw new ArgumentNullException(nameof(start), "Color.Lerp requires a start color.");
        }

        if (end == null)
        {
            throw new ArgumentNullException(nameof(end), "Color.Lerp requires an end color.");
        }

        if (double.IsNaN(t))
        {
            NodeWarnings.Add("The value t is not a number (NaN), so the start color is used. Check the input 't'.");
            t = 0d;
        }

        var clamped = t < 0d ? 0d : (t > 1d ? 1d : t);
        return new CamelGraphColor(
            LerpChannel(start.A, end.A, clamped),
            LerpChannel(start.R, end.R, clamped),
            LerpChannel(start.G, end.G, clamped),
            LerpChannel(start.B, end.B, clamped));
    }

    /// <summary>
    /// A pseudo-random color: the same seed always yields the same color (so
    /// re-running a graph never repaints everything), and nearby seeds give
    /// clearly different hues. Change the seed to get another color.
    /// </summary>
    /// <param name="seed">Any number; each seed maps to one fixed color.</param>
    /// <returns>The color.</returns>
    [NodeName("Color.Random")]
    [return: NodeName("color")]
    [NodeDescription("A pseudo-random color, stable per seed: the same seed always gives the same color (re-runs stay consistent); change the seed for another one.")]
    [NodeSearchTags("random", "seed", "generate", "any")]
    public static CamelGraphColor Random(int seed = 0)
    {
        return CategoricalColor(0, seed);
    }

    /// <summary>
    /// A list of visually distinct pseudo-random colors. Hues advance by the
    /// golden angle, so neighbours in the list never look alike; the same seed
    /// always yields the same list. A count of 0 or less gives an empty list; the most it makes is 1,000,000.
    /// </summary>
    /// <param name="count">How many colors to generate (0 gives an empty list; at most 1,000,000).</param>
    /// <param name="seed">Any number; each seed yields one fixed sequence.</param>
    /// <returns>The colors.</returns>
    [NodeName("Color.RandomList")]
    [return: NodeName("colors")]
    [NodeDescription(
        "A list of visually distinct pseudo-random colors (golden-angle hues), stable per seed — ideal for coloring N groups apart. " +
        "A count of 0 gives an empty list (no groups, no colors); the most it makes is 1,000,000.")]
    [NodeSearchTags("random", "list", "palette", "distinct", "generate", "series")]
    public static List<CamelGraphColor> RandomList([NodeRange(0, 1000000, SoftMin = 1, SoftMax = 50)] int count = 5, int seed = 0)
    {
        RequireCount(count, "Color.RandomList");

        var colors = new List<CamelGraphColor>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            colors.Add(CategoricalColor(i, seed));
        }

        return colors;
    }

    /// <summary>
    /// A list of colors evenly blended from a start color to an end color
    /// (both included). One color returns the start; two return exactly the
    /// endpoints. A count of 0 or less gives an empty list; the most it makes is 1,000,000.
    /// </summary>
    /// <param name="count">How many colors to generate (0 gives an empty list; at most 1,000,000).</param>
    /// <param name="start">First color of the gradient (empty = blue).</param>
    /// <param name="end">Last color of the gradient (empty = red).</param>
    /// <returns>The gradient colors, start to end.</returns>
    [NodeName("Color.Gradient")]
    [return: NodeName("colors")]
    [NodeDescription(
        "A list of N colors evenly blended between two colors (endpoints included; defaults blue to red) — for heat-map style " +
        "legends and value ramps. A count of 0 gives an empty list; the most it makes is 1,000,000.")]
    [NodeSearchTags("gradient", "ramp", "blend", "range", "between", "interpolate", "list", "series")]
    public static List<CamelGraphColor> Gradient(
        [NodeRange(0, 1000000, SoftMin = 1, SoftMax = 50)] int count = 5,
        CamelGraphColor? start = null,
        CamelGraphColor? end = null)
    {
        RequireCount(count, "Color.Gradient");

        var from = start ?? new CamelGraphColor(255, 29, 78, 216);  // blue
        var to = end ?? new CamelGraphColor(255, 220, 38, 38);      // red

        var colors = new List<CamelGraphColor>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            var t = count == 1 ? 0d : (double)i / (count - 1);
            colors.Add(Lerp(from, to, t));
        }

        return colors;
    }

    // A wrong count (billions) must not take the memory of the whole session; 0 or less is simply an empty list.
    private static void RequireCount(int count, string node)
    {
        if (count > MaxColors)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                node + " makes at most 1,000,000 colors; got " + count.ToString(CultureInfo.InvariantCulture) +
                ". A list that long would use too much memory: use a smaller count.");
        }
    }

    /// <summary>
    /// Maps every value to a color so equal values share a color — wire in
    /// parameter values (or cluster numbers) and get one color per entry, plus
    /// a value/color legend. Distinct values are numbered in order of first
    /// appearance and colored from a visually distinct palette (or from your
    /// own color list, cycled when shorter than the number of distinct values).
    /// </summary>
    /// <param name="values">One value per element (property values, numbers, names, ...).</param>
    /// <param name="colors">Optional palette: colors or "#RRGGBB" strings, used in order and cycled.</param>
    /// <returns>A color per input value, and the distinct values with their colors (index-aligned legend); all three are empty for an empty list.</returns>
    [NodeName("Color.ByValues")]
    [MultiReturn("colors", "uniqueValues", "uniqueColors")]
    [PortKinds("colour*", "", "colour*")]
    [NodeDescription(
        "One color per value, equal values sharing a color — feed parameter values in, feed the colors to Appearance.OverrideColor per group, " +
        "and use the uniqueValues/uniqueColors legend for reports. Optional own palette (cycled). An empty list of values (a search " +
        "that found nothing) gives empty lists, not an error.")]
    [NodeSearchTags("color", "by", "value", "parameter", "property", "categorical", "legend", "map", "group")]
    public static Dictionary<string, object?> ByValues(IList<object?> values, IList<object?>? colors = null)
    {
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "Color.ByValues needs a list of values. Wire the values to color into the 'values' input.");
        }

        var palette = new List<CamelGraphColor>();
        if (colors != null)
        {
            for (var i = 0; i < colors.Count; i++)
            {
                palette.Add(CoerceColor(colors[i], i));
            }
        }

        var indexOfValue = new Dictionary<string, int>(StringComparer.Ordinal);
        var uniqueValues = new List<object?>();
        var uniqueColors = new List<CamelGraphColor>();
        var result = new List<CamelGraphColor>(values.Count);

        foreach (var value in values)
        {
            var key = CamelGraph.Core.Types.TypeCoercion.FormatValue(value);
            if (!indexOfValue.TryGetValue(key, out var index))
            {
                index = uniqueValues.Count;
                indexOfValue[key] = index;
                uniqueValues.Add(value);
                uniqueColors.Add(palette.Count > 0
                    ? palette[index % palette.Count]
                    : CategoricalColor(index, seed: 0));
            }

            result.Add(uniqueColors[index]);
        }

        return new Dictionary<string, object?>
        {
            { "colors", result },
            { "uniqueValues", uniqueValues },
            { "uniqueColors", uniqueColors },
        };
    }

    /// <summary>
    /// The shared categorical generator: hue walks the golden angle from a
    /// seed-scrambled start (maximally separated neighbours), with a small
    /// deterministic saturation/value cycle for extra contrast on long lists.
    /// </summary>
    internal static CamelGraphColor CategoricalColor(int index, int seed)
    {
        // Scramble the seed so consecutive seeds land on unrelated hues.
        uint hash = (uint)seed * 2654435761u + 0x9E3779B9u;
        var start = (hash % 360u) / 360.0;
        var hue = (start + index * 0.6180339887498949) % 1.0;

        var saturation = 0.62 + 0.14 * (index % 3);   // 0.62 / 0.76 / 0.90
        var value = 0.92 - 0.10 * ((index / 3) % 2);  // 0.92 / 0.82

        return FromHsv(hue * 360.0, saturation, value);
    }

    /// <summary>HSV → RGB (h in degrees, s and v in 0–1), fully opaque.</summary>
    private static CamelGraphColor FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return new CamelGraphColor(
            255,
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    /// <summary>A palette entry as a color: a CamelGraphColor or a hex string.</summary>
    private static CamelGraphColor CoerceColor(object? entry, int index)
    {
        switch (entry)
        {
            case CamelGraphColor color:
                return color;
            case string hex when hex.Trim().Length > 0:
                try
                {
                    return FromHex(hex);
                }
                catch (FormatException)
                {
                    throw new ArgumentException(
                        "Color.ByValues: item " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of 'colors' ('" + Shown(hex) +
                        "') is not a hex color. Use \"#RRGGBB\" or \"#AARRGGBB\" text, or wire colors.");
                }

            default:
                throw new ArgumentException(
                    "Color.ByValues: item " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of 'colors' (" + Shown(entry) +
                    ") is not a color. Wire colors or \"#RRGGBB\" text; a list inside the list is not a color.");
        }
    }

    // The offending value for a message, short enough to read.
    private static string Shown(object? value)
    {
        var text = CamelGraph.Core.Types.TypeCoercion.FormatValue(value);
        return text.Length <= 40 ? text : text.Substring(0, 37) + "...";
    }

    private static int LerpChannel(byte from, byte to, double t)
    {
        return (int)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
    }
}
