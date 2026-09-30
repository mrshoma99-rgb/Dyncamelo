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
    private static readonly Dictionary<PortFamily, Brush> Brushes = BuildBrushes();
    private static readonly Geometry ItemGlyph = Freeze(new EllipseGeometry(new Point(5, 5), 4.6, 4.6));
    private static readonly Geometry ListGlyph = Freeze(new RectangleGeometry(new Rect(0.6, 0.6, 8.8, 8.8), 2, 2));
    private static readonly Geometry NestedGlyph = BuildNested();
    private static readonly Geometry UnknownGlyph = Freeze(Geometry.Parse("M5,0.3 L9.7,5 L5,9.7 L0.3,5 Z"));

    /// <summary>The frozen brush of a family.</summary>
    public static Brush For(PortFamily family)
    {
        return Brushes.TryGetValue(family, out var brush) ? brush : Brushes[PortFamily.Any];
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

    private static Dictionary<PortFamily, Brush> BuildBrushes()
    {
        var map = new Dictionary<PortFamily, Brush>();
        foreach (var family in PortKindPalette.Families)
        {
            var hex = PortKindPalette.Hex(family);
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
