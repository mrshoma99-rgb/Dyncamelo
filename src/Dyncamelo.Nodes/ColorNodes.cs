using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;

namespace Dyncamelo.Nodes;

/// <summary>
/// Color construction nodes. Colors are <see cref="DyncameloColor"/> values;
/// host layers (UI, Navisworks appearance overrides) convert them to their
/// native color types.
/// </summary>
[NodeCategory("Color")]
public static class ColorNodes
{
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
    public static DyncameloColor ByArgb(int a = 255, int r = 0, int g = 0, int b = 0)
    {
        return new DyncameloColor(a, r, g, b);
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
    public static DyncameloColor FromHex(string hex)
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
        return new DyncameloColor(
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
    [NodeDescription("Splits a color into its red, green, blue and alpha channels (0-255).")]
    [NodeSearchTags("deconstruct", "channels", "rgb", "argb")]
    public static Dictionary<string, object> Components(DyncameloColor color)
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
    /// <param name="t">Interpolation parameter (clamped to 0–1).</param>
    /// <returns>The interpolated color.</returns>
    [NodeName("Color.Lerp")]
    [return: NodeName("color")]
    [NodeDescription("Interpolates between two colors (t clamped to 0-1).")]
    [NodeSearchTags("interpolate", "blend", "gradient", "mix")]
    public static DyncameloColor Lerp(DyncameloColor start, DyncameloColor end, double t)
    {
        if (start == null)
        {
            throw new ArgumentNullException(nameof(start), "Color.Lerp requires a start color.");
        }

        if (end == null)
        {
            throw new ArgumentNullException(nameof(end), "Color.Lerp requires an end color.");
        }

        var clamped = t < 0d ? 0d : (t > 1d ? 1d : t);
        return new DyncameloColor(
            LerpChannel(start.A, end.A, clamped),
            LerpChannel(start.R, end.R, clamped),
            LerpChannel(start.G, end.G, clamped),
            LerpChannel(start.B, end.B, clamped));
    }

    private static int LerpChannel(byte from, byte to, double t)
    {
        return (int)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
    }
}
