using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Dyncamelo.UI.Services;

/// <summary>
/// A named UI colour palette: a colour for each <c>Dyc.*Brush</c> key in the
/// theme dictionary. Applied live by mutating the shared (unfrozen)
/// <see cref="SolidColorBrush"/> instances' <c>.Color</c> in place — the theme
/// is referenced entirely through StaticResource, so replacing dictionary
/// entries would not recolour already-rendered elements.
/// </summary>
public sealed class UiPalette
{
    /// <summary>Creates a palette.</summary>
    /// <param name="id">Stable id persisted in settings.</param>
    /// <param name="displayName">Label shown in the settings picker.</param>
    /// <param name="colors">Map of theme brush key (e.g. "Dyc.CanvasBrush") to colour.</param>
    public UiPalette(string id, string displayName, IReadOnlyDictionary<string, Color> colors)
    {
        Id = id;
        DisplayName = displayName;
        Colors = colors;
    }

    /// <summary>Stable id persisted in settings.</summary>
    public string Id { get; }

    /// <summary>Label shown in the settings picker.</summary>
    public string DisplayName { get; }

    /// <summary>Theme brush key → colour.</summary>
    public IReadOnlyDictionary<string, Color> Colors { get; }

    /// <summary>Accent colour used as the picker swatch.</summary>
    public Color AccentColor =>
        Colors.TryGetValue("Dyc.AccentBrush", out var c) ? c : System.Windows.Media.Colors.Gray;
}

/// <summary>
/// The built-in UI palettes offered by the settings panel. Every palette lists
/// a colour for all 18 theme brush keys; the "DyncameloDark" palette carries the
/// original dark values so switching back restores the theme exactly.
/// </summary>
public static class PaletteCatalog
{
    /// <summary>The theme brush keys a palette must define (all mutated on a swap).</summary>
    public static readonly IReadOnlyList<string> BrushKeys = new[]
    {
        "Dyc.CanvasBrush", "Dyc.GridLineBrush", "Dyc.PanelBrush", "Dyc.PanelBorderBrush",
        "Dyc.NodeBodyBrush", "Dyc.NodeBorderBrush", "Dyc.TextBrush", "Dyc.SubtleTextBrush",
        "Dyc.AccentBrush", "Dyc.SelectionBorderBrush", "Dyc.WireBrush", "Dyc.WarningBrush",
        "Dyc.ErrorBrush", "Dyc.InputBackgroundBrush", "Dyc.InputBorderBrush", "Dyc.HoverBrush",
        "Dyc.NoteBrush", "Dyc.NoteBorderBrush",
    };

    private static readonly List<UiPalette> _all = new List<UiPalette>
    {
        // Default — mirrors DyncameloDark.xaml's brush defaults (BIMCamel dark tokens);
        // switching back to this palette restores the theme exactly.
        Make("DyncameloDark", "Dyncamelo Dark",
            "#FF15171B", "#FF20242C", "#FF1C1F24", "#FF333941", "#FF23272E", "#FF3D444D",
            "#FFE7EAEE", "#FF9AA3AD", "#FF3AA0F0", "#993AA0F0", "#FF7E8896", "#FFF0C66A",
            "#FFEF5350", "#FF1B1E23", "#FF3D444D", "#FF1F2D3D", "#FF3A2F12", "#FF5C4A1E"),

        // Deep blue.
        Make("Midnight", "Midnight",
            "#FF0F1420", "#FF1A2130", "#FF141B2B", "#FF263248", "#FF1C2740", "#FF2E3E5C",
            "#FFE6EDF7", "#FF8B96AC", "#FF5B8DEF", "#995B8DEF", "#FF7E8AA6", "#FFF5A623",
            "#FFF0616D", "#FF0D1220", "#FF2E3E5C", "#FF24304A", "#FF2A3350", "#FF46567E"),

        // Warm neutral grey with a teal accent.
        Make("Slate", "Slate",
            "#FF202225", "#FF2A2D31", "#FF26292E", "#FF3A3F46", "#FF303439", "#FF454B54",
            "#FFE9EAEC", "#FFA0A4AB", "#FF4CC2A8", "#994CC2A8", "#FF949AA3", "#FFE0A02A",
            "#FFE15B5B", "#FF1C1E22", "#FF444A52", "#FF383D44", "#FF3A3A28", "#FF59573A"),

        // Light theme — BIMCamel light tokens (Bg #EEF1F5, Pane #F7F8FA, Card #FFF,
        // Text #1F2329, Accent #0070C0). Proper dark-on-light contrast throughout.
        Make("Light", "Light",
            "#FFEEF1F5", "#FFE1E5EA", "#FFF7F8FA", "#FFE1E5EA", "#FFFFFFFF", "#FFCDD3DA",
            "#FF1F2329", "#FF6B7480", "#FF0070C0", "#990070C0", "#FF8A93A0", "#FFB26B00",
            "#FFD33A3A", "#FFFFFFFF", "#FFCDD3DA", "#FFE6F0FA", "#FFFFF7E6", "#FFFFE1A8"),
    };

    /// <summary>All built-in palettes, in display order (default first).</summary>
    public static IReadOnlyList<UiPalette> All => _all;

    /// <summary>The default palette (the original dark theme).</summary>
    public static UiPalette Default => _all[0];

    /// <summary>Looks up a palette by id, or null when unknown.</summary>
    /// <param name="id">Palette id.</param>
    public static UiPalette? ById(string id)
    {
        foreach (var palette in _all)
        {
            if (string.Equals(palette.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return palette;
            }
        }

        return null;
    }

    // Positional builder: the hex strings are given in BrushKeys order.
    private static UiPalette Make(string id, string name, params string[] hex)
    {
        if (hex.Length != BrushKeys.Count)
        {
            throw new ArgumentException("Palette '" + id + "' must define " + BrushKeys.Count + " colours.");
        }

        var map = new Dictionary<string, Color>(BrushKeys.Count, StringComparer.Ordinal);
        for (int i = 0; i < BrushKeys.Count; i++)
        {
            map[BrushKeys[i]] = (Color)ColorConverter.ConvertFromString(hex[i]);
        }

        return new UiPalette(id, name, map);
    }
}
