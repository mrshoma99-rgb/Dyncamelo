using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Dyncamelo.Core.Editing;

namespace Dyncamelo.UI.Services;

/// <summary>
/// Shared, frozen brushes and socket glyphs for port families and depths.
/// Family colours are semantic (they do not follow the UI palette), so one
/// frozen instance per family serves every socket and wire on the canvas — no
/// per-binding allocation and no change tracking.
/// </summary>
public static class PortBrushes
{
    private static readonly Dictionary<PortFamily, Brush> DarkBrushes = BuildBrushes(PortKindPalette.Hex);
    private static readonly Dictionary<PortFamily, Brush> LightBrushes = BuildBrushes(PortKindPalette.HexOnLight);
    private static bool _onLight;
    private static readonly Geometry ItemGlyph = Freeze(new EllipseGeometry(new Point(5, 5), 4.6, 4.6));
    private static readonly Geometry ListGlyph = Freeze(new RectangleGeometry(new Rect(0.6, 0.6, 8.8, 8.8), 2, 2));
    private static readonly Geometry NestedGlyph = BuildNested();
    private static readonly Geometry UnknownGlyph = Freeze(Geometry.Parse("M5,0.3 L9.7,5 L5,9.7 L0.3,5 Z"));

    /// <summary>True while a light palette is active: sockets and wires use the darker family colours.</summary>
    public static bool OnLight
    {
        get => _onLight;
        set => _onLight = value;
    }

    /// <summary>The frozen brush of a family (the light-canvas variant while a light palette is active).</summary>
    public static Brush For(PortFamily family)
    {
        var set = _onLight ? LightBrushes : DarkBrushes;
        return set.TryGetValue(family, out var brush) ? brush : set[PortFamily.Any];
    }

    /// <summary>The frozen 10×10 socket glyph for a structure.</summary>
    public static Geometry Glyph(PortDepth depth)
    {
        switch (depth)
        {
            case PortDepth.List: return ListGlyph;
            case PortDepth.Nested: return NestedGlyph;
            case PortDepth.Unknown: return UnknownGlyph;
            default: return ItemGlyph;
        }
    }

    private static Dictionary<PortFamily, Brush> BuildBrushes(System.Func<PortFamily, string> hexOf)
    {
        var map = new Dictionary<PortFamily, Brush>();
        foreach (var family in PortKindPalette.Families)
        {
            var hex = hexOf(family);
            var color = Color.FromRgb(
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            map[family] = brush;
        }

        return map;
    }

    private static readonly Dictionary<int, Geometry> Pills = new Dictionary<int, Geometry>();

    /// <summary>
    /// The frozen pill of a multi-input socket, 8.8 wide and <paramref name="height"/> tall (rounded to whole pixels):
    /// one elongated socket that several wires land on.
    /// </summary>
    public static Geometry Pill(double height)
    {
        var key = (int)System.Math.Round(System.Math.Max(14d, height));
        if (!Pills.TryGetValue(key, out var pill))
        {
            pill = Freeze(new RectangleGeometry(new Rect(0.6, 0.6, 8.8, key - 1.2), 4.4, 4.4));
            Pills[key] = pill;
        }

        return pill;
    }

    private static Geometry BuildNested()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0.6, 0.6, 8.8, 8.8), 2, 2));
        group.Children.Add(new RectangleGeometry(new Rect(3.0, 3.0, 4.0, 4.0), 1, 1));
        return Freeze(group);
    }

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
