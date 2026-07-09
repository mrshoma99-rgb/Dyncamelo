using System;
using System.Globalization;

namespace Dyncamelo.Nodes;

/// <summary>
/// A simple immutable ARGB color used by the Color nodes. Defined here (rather
/// than borrowing <c>System.Drawing</c> or WPF types) so the node library stays
/// dependency-free on netstandard2.0; host layers convert to their native
/// color types at the boundary.
/// </summary>
public sealed class DyncameloColor : IEquatable<DyncameloColor>
{
    /// <summary>Creates a color, clamping every channel to the 0–255 range.</summary>
    /// <param name="a">Alpha channel (0 = transparent, 255 = opaque).</param>
    /// <param name="r">Red channel.</param>
    /// <param name="g">Green channel.</param>
    /// <param name="b">Blue channel.</param>
    public DyncameloColor(int a, int r, int g, int b)
    {
        A = ClampChannel(a);
        R = ClampChannel(r);
        G = ClampChannel(g);
        B = ClampChannel(b);
    }

    /// <summary>Alpha channel (0–255).</summary>
    public byte A { get; }

    /// <summary>Red channel (0–255).</summary>
    public byte R { get; }

    /// <summary>Green channel (0–255).</summary>
    public byte G { get; }

    /// <summary>Blue channel (0–255).</summary>
    public byte B { get; }

    /// <inheritdoc />
    public bool Equals(DyncameloColor? other)
    {
        return other != null && A == other.A && R == other.R && G == other.G && B == other.B;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DyncameloColor);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return (A << 24) | (R << 16) | (G << 8) | B;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "Color(A={0}, R={1}, G={2}, B={3})", A, R, G, B);
    }

    private static byte ClampChannel(int value)
    {
        return (byte)Math.Max(0, Math.Min(255, value));
    }
}
