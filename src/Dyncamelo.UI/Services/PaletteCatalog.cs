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
        // Original theme values (DyncameloDark.xaml lines 13-31) — the default.
        Make("DyncameloDark", "Dyncamelo Dark",
            "#FF17191E", "#FF23262E", "#FF1F222A", "#FF333843", "#FF2A2E38", "#FF3A4050",
            "#FFE8EAEE", "#FF9AA1AE", "#FF1FAEFF", "#991FAEFF", "#FF8B94A5", "#FFF59E0B",
            "#FFEF4444", "#FF1B1E25", "#FF3A4050", "#FF343B49", "#FF4A452A", "#FF6B6236"),

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

        // Light theme.
        Make("Light", "Light",
            "#FFF4F5F7", "#FFE4E7EC", "#FFFFFFFF", "#FFD5DAE1", "#FFFFFFFF", "#FFCBD2DB",
            "#FF1E2430", "#FF5D6675", "#FF1F82D8", "#991F82D8", "#FF9AA3B2", "#FFC77A0A",
            "#FFD33A3A", "#FFFFFFFF", "#FFC4CCD6", "#FFE8ECF2", "#FFFBF3CC", "#FFE4D383"),
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
