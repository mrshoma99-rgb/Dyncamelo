using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes;

/// <summary>
/// More color nodes: hex output, HSV construction and decomposition, lighten /
/// darken / invert / alpha adjustments, ready-made palettes and a black-or-white
/// label colour for any fill. Every node is pure: colors are immutable
/// <see cref="CamelGraphColor"/> values and each node returns a new one.
/// </summary>
[NodeCategory("Color")]
public static class ColorExtraNodes
{
    /// <summary>The palette names the node accepts, in the order the editor lists them.</summary>
    private const string PaletteNames = "colourblind, tableau, pastel, status, viridis, heat, grey";

    // Categorical palettes (cycled when more colors are requested than they hold), as 0xRRGGBB.
    // Okabe-Ito colour-blind-safe set, in the authors' order.
    private static readonly uint[] OkabeIto =
    {
        0x000000, 0xE69F00, 0x56B4E9, 0x009E73, 0xF0E442, 0x0072B2, 0xD55E00, 0xCC79A7,
    };

    // Tableau 10 (the classic "Tableau 10" colours).
    private static readonly uint[] Tableau10 =
    {
        0x4E79A7, 0xF28E2B, 0xE15759, 0x76B7B2, 0x59A14F, 0xEDC948, 0xB07AA1, 0xFF9DA7, 0x9C755F, 0xBAB0AC,
    };

    // ColorBrewer "Pastel1".
    private static readonly uint[] Pastel =
    {
        0xFBB4AE, 0xB3CDE3, 0xCCEBC5, 0xDECBE4, 0xFED9A6, 0xFFFFCC, 0xE5D8BD, 0xFDDAEC, 0xF2F2F2,
    };

    // Traffic-light status colors: good, warning, bad, unknown.
    private static readonly uint[] Status =
    {
        0x2F9E44, 0xF59F00, 0xE03131, 0x868E96,
    };

    // Sequential ramps: anchor colors that are linearly interpolated (in RGB) to the requested count.
    // Viridis (9 anchors), low = dark purple, high = yellow.
    private static readonly uint[] Viridis =
    {
        0x440154, 0x472D7B, 0x3B528B, 0x2C728E, 0x21908C, 0x27AD81, 0x5CC863, 0xAADC32, 0xFDE725,
    };

    // ColorBrewer "YlOrRd": low = pale yellow, high = dark red.
    private static readonly uint[] Heat =
    {
        0xFFFFCC, 0xFFEDA0, 0xFED976, 0xFEB24C, 0xFD8D3C, 0xFC4E2A, 0xE31A1C, 0xBD0026, 0x800026,
    };

    // Low = white, high = black.
    private static readonly uint[] Grey =
    {
        0xFFFFFF, 0x000000,
    };

    /// <summary>
    /// Formats a color as a hex string, "#RRGGBB" by default or "#AARRGGBB" when the alpha channel
    /// is included. The result is upper case and parses back with Color.FromHex.
    /// </summary>
    /// <param name="color">The color to format.</param>
    /// <param name="includeAlpha">True to prefix the alpha channel ("#AARRGGBB"); false drops it ("#RRGGBB").</param>
    /// <returns>The hex text.</returns>
    [NodeName("Color.ToHex")]
    [return: NodeName("hex")]
    [NodeDescription("Formats a color as hex text, \"#RRGGBB\" (or \"#AARRGGBB\" with includeAlpha) — the reverse of Color.FromHex.")]
    [NodeSearchTags("hex", "html", "web", "string", "text", "format", "css")]
    public static string ToHex(CamelGraphColor color, bool includeAlpha = false)
    {
        RequireColor(color, "Color.ToHex", nameof(color));

        var text = "#" + (includeAlpha ? Hex2(color.A) : string.Empty) + Hex2(color.R) + Hex2(color.G) + Hex2(color.B);
        return text;
    }

    /// <summary>
    /// Builds a color from hue, saturation and value. Hue is an angle in degrees and wraps around
    /// (360 = 0, -90 = 270); saturation and value are clamped to 0–1.
    /// </summary>
    /// <param name="hue">Hue in degrees (0 = red, 120 = green, 240 = blue); wraps around.</param>
    /// <param name="saturation">Saturation, 0 (grey) to 1 (vivid); clamped.</param>
    /// <param name="value">Value (brightness), 0 (black) to 1 (full); clamped.</param>
    /// <param name="alpha">Alpha channel, 0–255 (default fully opaque).</param>
    /// <returns>The color.</returns>
    [NodeName("Color.ByHSV")]
    [return: NodeName("color")]
    [NodeDescription("Creates a color from hue (degrees, wraps around), saturation and value (0-1, clamped).")]
    [NodeSearchTags("hsv", "hsb", "hue", "saturation", "brightness", "create")]
    public static CamelGraphColor ByHsv(double hue, double saturation, double value, [NodeRange(0, 255)] int alpha = 255)
    {
        RequireFinite(hue, "Color.ByHSV", nameof(hue));
        RequireNumber(saturation, "Color.ByHSV", nameof(saturation));
        RequireNumber(value, "Color.ByHSV", nameof(value));

        return FromHsv(hue, Clamp01(saturation), Clamp01(value), alpha);
    }

    /// <summary>Decomposes a color into hue (degrees), saturation and value (0–1).</summary>
    /// <param name="color">The color to decompose.</param>
    /// <returns>Dictionary with "hue" (0–360, 0 for greys), "saturation" and "value" (0–1).</returns>
    [NodeName("Color.ToHSV")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [MultiReturn("hue", "saturation", "value")]
    [PortKinds("number", "number", "number")]
    [NodeDescription("Splits a color into hue (degrees, 0-360), saturation and value (0-1); greys have hue 0.")]
    [NodeSearchTags("hsv", "hsb", "hue", "saturation", "brightness", "deconstruct", "components")]
    public static Dictionary<string, object> ToHsv(CamelGraphColor color)
    {
        RequireColor(color, "Color.ToHSV", nameof(color));

        Channels(color, out var r, out var g, out var b);
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));

        return new Dictionary<string, object>
        {
            ["hue"] = Hue(r, g, b, max, delta),
            ["saturation"] = max <= 0d ? 0d : delta / max,
            ["value"] = max,
        };
    }

    /// <summary>
    /// Makes a color lighter by raising its HSL lightness by an amount (0–1, clamped).
    /// Hue and saturation are kept; so is alpha.
    /// </summary>
    /// <param name="color">The color to lighten.</param>
    /// <param name="amount">How much lightness to add, 0 (none) to 1 (white); clamped.</param>
    /// <returns>The lighter color.</returns>
    [NodeName("Color.Lighten")]
    [return: NodeName("color")]
    [NodeDescription("Makes a color lighter by shifting its HSL lightness up by amount (0-1, clamped); alpha is kept.")]
    [NodeSearchTags("lighter", "brighten", "tint", "pale", "lightness", "hsl")]
    public static CamelGraphColor Lighten(CamelGraphColor color, double amount = 0.2)
    {
        RequireColor(color, "Color.Lighten", nameof(color));
        RequireNumber(amount, "Color.Lighten", nameof(amount));

        return ShiftLightness(color, Clamp01(amount));
    }

    /// <summary>
    /// Makes a color darker by lowering its HSL lightness by an amount (0–1, clamped).
    /// Hue and saturation are kept; so is alpha.
    /// </summary>
    /// <param name="color">The color to darken.</param>
    /// <param name="amount">How much lightness to remove, 0 (none) to 1 (black); clamped.</param>
    /// <returns>The darker color.</returns>
    [NodeName("Color.Darken")]
    [return: NodeName("color")]
    [NodeDescription("Makes a color darker by shifting its HSL lightness down by amount (0-1, clamped); alpha is kept.")]
    [NodeSearchTags("darker", "shade", "dim", "lightness", "hsl")]
    public static CamelGraphColor Darken(CamelGraphColor color, double amount = 0.2)
    {
        RequireColor(color, "Color.Darken", nameof(color));
        RequireNumber(amount, "Color.Darken", nameof(amount));

        return ShiftLightness(color, -Clamp01(amount));
    }

    /// <summary>Returns the same color with a different alpha (opacity) channel.</summary>
    /// <param name="color">The color to change.</param>
    /// <param name="alpha">The new alpha, 0 (transparent) to 255 (opaque); clamped.</param>
    /// <returns>The color with the new alpha.</returns>
    [NodeName("Color.WithAlpha")]
    [return: NodeName("color")]
    [NodeDescription("Returns a color with its alpha (opacity) replaced, 0 = transparent to 255 = opaque; red, green and blue are unchanged.")]
    [NodeSearchTags("alpha", "opacity", "transparency", "transparent", "set")]
    public static CamelGraphColor WithAlpha(CamelGraphColor color, [NodeRange(0, 255)] int alpha)
    {
        RequireColor(color, "Color.WithAlpha", nameof(color));

        return new CamelGraphColor(alpha, color.R, color.G, color.B);
    }

    /// <summary>Inverts the red, green and blue channels (255 minus each); alpha is kept.</summary>
    /// <param name="color">The color to invert.</param>
    /// <returns>The inverted color.</returns>
    [NodeName("Color.Invert")]
    [return: NodeName("color")]
    [NodeDescription("Inverts a color's red, green and blue channels (the photographic negative); alpha is kept.")]
    [NodeSearchTags("negative", "opposite", "complement", "flip", "reverse")]
    public static CamelGraphColor Invert(CamelGraphColor color)
    {
        RequireColor(color, "Color.Invert", nameof(color));

        return new CamelGraphColor(color.A, 255 - color.R, 255 - color.G, 255 - color.B);
    }

    /// <summary>
    /// A named palette as a list of colors. The categorical palettes cycle when more colors are
    /// requested than they hold: "colourblind" (Okabe-Ito, 8 colors: black, orange, sky blue, bluish
    /// green, yellow, blue, vermillion, reddish purple), "tableau" (the 10 Tableau colors), "pastel"
    /// (9 soft colors) and "status" (green, amber, red, grey for good / warning / bad / unknown).
    /// The sequential ramps are interpolated to exactly the requested count, low to high: "viridis"
    /// (dark purple to yellow), "heat" (pale yellow to dark red) and "grey" (white to black). Names are
    /// not case-sensitive.
    /// </summary>
    /// <param name="name">Palette name: colourblind, tableau, pastel, status, viridis, heat or grey.</param>
    /// <param name="count">How many colors to return (1–256).</param>
    /// <returns>The palette colors.</returns>
    [NodeName("Color.Palette")]
    [return: NodeName("colors")]
    [NodeDescription("A named palette as a list of colors: colourblind-safe, tableau, pastel, status (green/amber/red/grey) or the viridis, heat and grey ramps interpolated to count; fixed palettes cycle past their size.")]
    [NodeSearchTags("palette", "scheme", "colourblind", "colorblind", "okabe", "ito", "tableau", "viridis", "heatmap", "gray", "ramp", "categorical", "legend", "list")]
    public static List<CamelGraphColor> Palette(
        [NodeChoices("colourblind", "tableau", "pastel", "status", "viridis", "heat", "grey")] string name = "colourblind",
        [NodeRange(1, 256)] int count = 8)
    {
        if (count < 1 || count > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Color.Palette needs a count between 1 and 256 (got " + count.ToString(CultureInfo.InvariantCulture) + ").");
        }

        var key = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (key == "colorblind")
        {
            key = "colourblind";
        }
        else if (key == "gray")
        {
            key = "grey";
        }

        uint[]? fixedColors = null;
        uint[]? ramp = null;
        switch (key)
        {
            case "colourblind":
                fixedColors = OkabeIto;
                break;
            case "tableau":
                fixedColors = Tableau10;
                break;
            case "pastel":
                fixedColors = Pastel;
                break;
            case "status":
                fixedColors = Status;
                break;
            case "viridis":
                ramp = Viridis;
                break;
            case "heat":
                ramp = Heat;
                break;
            case "grey":
                ramp = Grey;
                break;
            default:
                throw new ArgumentException(
                    (key.Length == 0
                        ? "Color.Palette needs a palette name."
                        : "Color.Palette does not know a palette called '" + name!.Trim() + "'.") +
                    " Valid names: " + PaletteNames + ".",
                    nameof(name));
        }

        var colors = new List<CamelGraphColor>(count);
        for (int i = 0; i < count; i++)
        {
            colors.Add(fixedColors != null
                ? FromRgb(fixedColors[i % fixedColors.Length])
                : Sample(ramp!, i, count));
        }

        return colors;
    }

    /// <summary>
    /// Picks black or white text for a background color: whichever has the higher WCAG contrast
    /// ratio against it (computed from the background's relative luminance; its alpha is ignored).
    /// </summary>
    /// <param name="background">The fill color the text will sit on.</param>
    /// <returns>Opaque black or opaque white.</returns>
    [NodeName("Color.ContrastText")]
    [return: NodeName("color")]
    [NodeDescription("Black or white, whichever reads better on a background color (WCAG contrast) — for labels on coloured fills and legends.")]
    [NodeSearchTags("contrast", "text", "label", "legible", "readable", "accessibility", "wcag", "black or white", "foreground")]
    public static CamelGraphColor ContrastText(CamelGraphColor background)
    {
        RequireColor(background, "Color.ContrastText", nameof(background));

        var luminance = RelativeLuminance(background);

        // Contrast with black is (L + 0.05) / 0.05, with white 1.05 / (L + 0.05).
        var contrastWithBlack = (luminance + 0.05) / 0.05;
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        return contrastWithBlack >= contrastWithWhite
            ? new CamelGraphColor(255, 0, 0, 0)
            : new CamelGraphColor(255, 255, 255, 255);
    }

    // ------------------------------------------------------------------ helpers

    private static void RequireColor(CamelGraphColor? color, string node, string parameter)
    {
        if (color == null)
        {
            throw new ArgumentNullException(parameter, node + " requires a color. Wire a color into the '" + parameter + "' input.");
        }
    }

    private static void RequireFinite(double value, string node, string parameter)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException(node + " needs a finite number for '" + parameter + "' (got " + FormatNumber(value) + ").", parameter);
        }
    }

    private static void RequireNumber(double value, string node, string parameter)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentException(node + " needs a number for '" + parameter + "' (got NaN).", parameter);
        }
    }

    private static string FormatNumber(double value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Hex2(byte channel)
    {
        return channel.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static double Clamp01(double value)
    {
        return value < 0d ? 0d : (value > 1d ? 1d : value);
    }

    private static void Channels(CamelGraphColor color, out double r, out double g, out double b)
    {
        r = color.R / 255d;
        g = color.G / 255d;
        b = color.B / 255d;
    }

    /// <summary>Hue in degrees, [0, 360); 0 for greys.</summary>
    private static double Hue(double r, double g, double b, double max, double delta)
    {
        if (delta <= 0d)
        {
            return 0d;
        }

        double sector;
        if (max == r)
        {
            sector = (g - b) / delta % 6d;
        }
        else if (max == g)
        {
            sector = (b - r) / delta + 2d;
        }
        else
        {
            sector = (r - g) / delta + 4d;
        }

        var hue = sector * 60d;
        if (hue < 0d)
        {
            hue += 360d;
        }

        return hue >= 360d ? hue - 360d : hue;
    }

    private static CamelGraphColor ShiftLightness(CamelGraphColor color, double delta)
    {
        Channels(color, out var r, out var g, out var b);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;
        var lightness = (max + min) / 2d;

        var spread = 1d - Math.Abs(2d * lightness - 1d);
        var saturation = chroma <= 0d || spread <= 0d ? 0d : Clamp01(chroma / spread);

        return FromHsl(Hue(r, g, b, max, chroma), saturation, Clamp01(lightness + delta), color.A);
    }

    /// <summary>HSV to color. The hue wraps; saturation and value must already be in 0–1.</summary>
    private static CamelGraphColor FromHsv(double hue, double saturation, double value, int alpha)
    {
        var chroma = value * saturation;
        return FromChroma(hue, chroma, value - chroma, alpha);
    }

    /// <summary>HSL to color. The hue wraps; saturation and lightness must already be in 0–1.</summary>
    private static CamelGraphColor FromHsl(double hue, double saturation, double lightness, int alpha)
    {
        var chroma = (1d - Math.Abs(2d * lightness - 1d)) * saturation;
        return FromChroma(hue, chroma, lightness - chroma / 2d, alpha);
    }

    /// <summary>Shared tail of the HSV / HSL conversions: chroma, the matching offset m, and the hue sector.</summary>
    private static CamelGraphColor FromChroma(double hue, double chroma, double m, int alpha)
    {
        var wrapped = hue % 360d;
        if (wrapped < 0d)
        {
            wrapped += 360d;
        }

        if (wrapped >= 360d)
        {
            wrapped = 0d;
        }

        var sector = wrapped / 60d;
        var x = chroma * (1d - Math.Abs(sector % 2d - 1d));

        double r, g, b;
        if (sector < 1d) { r = chroma; g = x; b = 0d; }
        else if (sector < 2d) { r = x; g = chroma; b = 0d; }
        else if (sector < 3d) { r = 0d; g = chroma; b = x; }
        else if (sector < 4d) { r = 0d; g = x; b = chroma; }
        else if (sector < 5d) { r = x; g = 0d; b = chroma; }
        else { r = chroma; g = 0d; b = x; }

        return new CamelGraphColor(alpha, ToChannel(r + m), ToChannel(g + m), ToChannel(b + m));
    }

    private static int ToChannel(double unit)
    {
        return (int)Math.Round(unit * 255d, MidpointRounding.AwayFromZero);
    }

    private static CamelGraphColor FromRgb(uint rgb)
    {
        return new CamelGraphColor(255, (int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF));
    }

    /// <summary>The index-th of count evenly spaced samples along a ramp of anchor colors (ends included).</summary>
    private static CamelGraphColor Sample(uint[] anchors, int index, int count)
    {
        if (count == 1)
        {
            return FromRgb(anchors[0]);
        }

        var position = (double)index / (count - 1) * (anchors.Length - 1);
        var lower = (int)Math.Floor(position);
        if (lower >= anchors.Length - 1)
        {
            return FromRgb(anchors[anchors.Length - 1]);
        }

        var t = position - lower;
        var from = FromRgb(anchors[lower]);
        var to = FromRgb(anchors[lower + 1]);
        return new CamelGraphColor(
            255,
            Mix(from.R, to.R, t),
            Mix(from.G, to.G, t),
            Mix(from.B, to.B, t));
    }

    private static int Mix(byte from, byte to, double t)
    {
        return (int)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
    }

    /// <summary>WCAG 2.x relative luminance of the color's sRGB channels (0 = black, 1 = white).</summary>
    private static double RelativeLuminance(CamelGraphColor color)
    {
        return 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);
    }

    private static double Linearize(byte channel)
    {
        var c = channel / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
