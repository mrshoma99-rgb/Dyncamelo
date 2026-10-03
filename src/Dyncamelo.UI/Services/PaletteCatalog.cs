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

    /// <summary>True when the canvas is light (drives darker socket/wire colours).</summary>
    public bool IsLight
    {
        get
        {
            if (!Colors.TryGetValue("Dyc.CanvasBrush", out var c))
            {
                return false;
            }

            // Relative luminance, good enough for "is this canvas light?".
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255d > 0.5;
        }
    }

    /// <summary>Accent colour used as the picker swatch.</summary>
    public Color AccentColor =>
        Colors.TryGetValue("Dyc.AccentBrush", out var c) ? c : System.Windows.Media.Colors.Gray;
}

/// <summary>
/// The built-in UI palettes offered by the settings panel. Every palette defines a colour for every key in
/// <see cref="Keys"/> (a test enforces it); "DyncameloDark" carries the original dark values so switching back
/// restores the theme exactly. Keys ending in "Color" are <c>Color</c> resources, the rest solid brushes.
/// </summary>
public static class PaletteCatalog
{
    /// <summary>Every theme resource key a palette must define.</summary>
    public static readonly IReadOnlyList<string> Keys = new[]
    {
        // canvas, panels, text
        "Dyc.CanvasBrush", "Dyc.GridLineBrush", "Dyc.PanelBrush", "Dyc.PanelBorderBrush",
        "Dyc.NodeBodyBrush", "Dyc.NodeBorderBrush", "Dyc.TextBrush", "Dyc.SubtleTextBrush",
        "Dyc.AccentBrush", "Dyc.SelectionBorderBrush", "Dyc.WireBrush", "Dyc.WarningBrush",
        "Dyc.ErrorBrush", "Dyc.InputBackgroundBrush", "Dyc.InputBorderBrush", "Dyc.HoverBrush",
        "Dyc.NoteBrush", "Dyc.NoteBorderBrush", "Dyc.SocketOutlineBrush",
        // full-canvas overlays (help, settings) and the minimap
        "Dyc.OverlayBrush", "Dyc.MinimapBrush", "Dyc.MinimapItemBrush", "Dyc.ViewportFillBrush",
        // the header (brand bar): gradient ends, text on it, and its hover chips
        "Dyc.BrandStartColor", "Dyc.BrandEndColor", "Dyc.OnBrandBrush", "Dyc.OnBrandSubtleBrush",
        "Dyc.BrandButtonBrush", "Dyc.BrandButtonHoverBrush",
        // Create / Modify / Info tints in the library
        "Dyc.FnCreateBrush", "Dyc.FnModifyBrush", "Dyc.FnInfoBrush",
        // the main action (the Run button): its fill and the text on it
        "Dyc.PrimaryBrush", "Dyc.OnPrimaryBrush",
    };

    /// <summary>The brush keys (kept for callers that predate colour keys).</summary>
    public static IReadOnlyList<string> BrushKeys => Keys;

    private static readonly List<UiPalette> _all = new List<UiPalette>
    {
        // Default — mirrors DyncameloDark.xaml's defaults (BIMCamel dark tokens); switching back restores the theme exactly.
        Make("DyncameloDark", "Dyncamelo Dark", new Dictionary<string, string>
        {
            ["Dyc.CanvasBrush"] = "#FF15171B", ["Dyc.GridLineBrush"] = "#FF20242C", ["Dyc.PanelBrush"] = "#FF1C1F24",
            ["Dyc.PanelBorderBrush"] = "#FF333941", ["Dyc.NodeBodyBrush"] = "#FF23272E", ["Dyc.NodeBorderBrush"] = "#FF3D444D",
            ["Dyc.TextBrush"] = "#FFE7EAEE", ["Dyc.SubtleTextBrush"] = "#FF9AA3AD", ["Dyc.AccentBrush"] = "#FF3AA0F0",
            ["Dyc.SelectionBorderBrush"] = "#993AA0F0", ["Dyc.WireBrush"] = "#FF7E8896", ["Dyc.WarningBrush"] = "#FFF0C66A",
            ["Dyc.ErrorBrush"] = "#FFEF5350", ["Dyc.InputBackgroundBrush"] = "#FF1B1E23", ["Dyc.InputBorderBrush"] = "#FF3D444D",
            ["Dyc.HoverBrush"] = "#FF1F2D3D", ["Dyc.NoteBrush"] = "#FF3A2F12", ["Dyc.NoteBorderBrush"] = "#FF5C4A1E", ["Dyc.SocketOutlineBrush"] = "#FF23272E",
            ["Dyc.OverlayBrush"] = "#F2151719", ["Dyc.MinimapBrush"] = "#D9151719", ["Dyc.MinimapItemBrush"] = "#FF7F8FA3",
            ["Dyc.ViewportFillBrush"] = "#223AA0F0",
            ["Dyc.BrandStartColor"] = "#FF0070C0", ["Dyc.BrandEndColor"] = "#FF0D83DA", ["Dyc.OnBrandBrush"] = "#FFFFFFFF",
            ["Dyc.OnBrandSubtleBrush"] = "#DDFFFFFF", ["Dyc.BrandButtonBrush"] = "#29FFFFFF", ["Dyc.BrandButtonHoverBrush"] = "#47FFFFFF",
            ["Dyc.FnCreateBrush"] = "#FF66BB6A", ["Dyc.FnModifyBrush"] = "#FFF0C66A", ["Dyc.FnInfoBrush"] = "#FF3AA0F0",
            ["Dyc.PrimaryBrush"] = "#FF1A73C5", ["Dyc.OnPrimaryBrush"] = "#FFFFFFFF",
        }),

        // Deep blue.
        Make("Midnight", "Midnight", new Dictionary<string, string>
        {
            ["Dyc.CanvasBrush"] = "#FF0F1420", ["Dyc.GridLineBrush"] = "#FF1A2130", ["Dyc.PanelBrush"] = "#FF141B2B",
            ["Dyc.PanelBorderBrush"] = "#FF263248", ["Dyc.NodeBodyBrush"] = "#FF1C2740", ["Dyc.NodeBorderBrush"] = "#FF2E3E5C",
            ["Dyc.TextBrush"] = "#FFE6EDF7", ["Dyc.SubtleTextBrush"] = "#FF8B96AC", ["Dyc.AccentBrush"] = "#FF5B8DEF",
            ["Dyc.SelectionBorderBrush"] = "#995B8DEF", ["Dyc.WireBrush"] = "#FF7E8AA6", ["Dyc.WarningBrush"] = "#FFF5A623",
            ["Dyc.ErrorBrush"] = "#FFF0616D", ["Dyc.InputBackgroundBrush"] = "#FF0D1220", ["Dyc.InputBorderBrush"] = "#FF2E3E5C",
            ["Dyc.HoverBrush"] = "#FF24304A", ["Dyc.NoteBrush"] = "#FF2A3350", ["Dyc.NoteBorderBrush"] = "#FF46567E", ["Dyc.SocketOutlineBrush"] = "#FF1C2740",
            ["Dyc.OverlayBrush"] = "#F20F1420", ["Dyc.MinimapBrush"] = "#D90F1420", ["Dyc.MinimapItemBrush"] = "#FF8B96AC",
            ["Dyc.ViewportFillBrush"] = "#225B8DEF",
            ["Dyc.BrandStartColor"] = "#FF0070C0", ["Dyc.BrandEndColor"] = "#FF0D83DA", ["Dyc.OnBrandBrush"] = "#FFFFFFFF",
            ["Dyc.OnBrandSubtleBrush"] = "#DDFFFFFF", ["Dyc.BrandButtonBrush"] = "#29FFFFFF", ["Dyc.BrandButtonHoverBrush"] = "#47FFFFFF",
            ["Dyc.FnCreateBrush"] = "#FF66BB6A", ["Dyc.FnModifyBrush"] = "#FFF5A623", ["Dyc.FnInfoBrush"] = "#FF5B8DEF",
            ["Dyc.PrimaryBrush"] = "#FF3D6FD6", ["Dyc.OnPrimaryBrush"] = "#FFFFFFFF",
        }),

        // Warm neutral grey with a teal accent.
        Make("Slate", "Slate", new Dictionary<string, string>
        {
            ["Dyc.CanvasBrush"] = "#FF202225", ["Dyc.GridLineBrush"] = "#FF2A2D31", ["Dyc.PanelBrush"] = "#FF26292E",
            ["Dyc.PanelBorderBrush"] = "#FF3A3F46", ["Dyc.NodeBodyBrush"] = "#FF303439", ["Dyc.NodeBorderBrush"] = "#FF454B54",
            ["Dyc.TextBrush"] = "#FFE9EAEC", ["Dyc.SubtleTextBrush"] = "#FFA0A4AB", ["Dyc.AccentBrush"] = "#FF4CC2A8",
            ["Dyc.SelectionBorderBrush"] = "#994CC2A8", ["Dyc.WireBrush"] = "#FF949AA3", ["Dyc.WarningBrush"] = "#FFE0A02A",
            ["Dyc.ErrorBrush"] = "#FFE15B5B", ["Dyc.InputBackgroundBrush"] = "#FF1C1E22", ["Dyc.InputBorderBrush"] = "#FF444A52",
            ["Dyc.HoverBrush"] = "#FF383D44", ["Dyc.NoteBrush"] = "#FF3A3A28", ["Dyc.NoteBorderBrush"] = "#FF59573A", ["Dyc.SocketOutlineBrush"] = "#FF303439",
            ["Dyc.OverlayBrush"] = "#F2202225", ["Dyc.MinimapBrush"] = "#D9202225", ["Dyc.MinimapItemBrush"] = "#FFA0A4AB",
            ["Dyc.ViewportFillBrush"] = "#224CC2A8",
            ["Dyc.BrandStartColor"] = "#FF0070C0", ["Dyc.BrandEndColor"] = "#FF0D83DA", ["Dyc.OnBrandBrush"] = "#FFFFFFFF",
            ["Dyc.OnBrandSubtleBrush"] = "#DDFFFFFF", ["Dyc.BrandButtonBrush"] = "#29FFFFFF", ["Dyc.BrandButtonHoverBrush"] = "#47FFFFFF",
            ["Dyc.FnCreateBrush"] = "#FF66BB6A", ["Dyc.FnModifyBrush"] = "#FFE0A02A", ["Dyc.FnInfoBrush"] = "#FF4CC2A8",
            ["Dyc.PrimaryBrush"] = "#FF4CC2A8", ["Dyc.OnPrimaryBrush"] = "#FF0B1F1A",
        }),

        // Light theme — BIMCamel light tokens (Bg #EEF1F5, Pane #F7F8FA, Card #FFF, Text #1F2329, Accent #0070C0).
        // The header is light too (dark text on a white-to-grey gradient), and the Create/Modify/Info tints are darkened
        // so they read on white.
        Make("Light", "Light", new Dictionary<string, string>
        {
            ["Dyc.CanvasBrush"] = "#FFEEF1F5", ["Dyc.GridLineBrush"] = "#FFE1E5EA", ["Dyc.PanelBrush"] = "#FFF7F8FA",
            ["Dyc.PanelBorderBrush"] = "#FFD5DAE1", ["Dyc.NodeBodyBrush"] = "#FFFFFFFF", ["Dyc.NodeBorderBrush"] = "#FFCDD3DA",
            ["Dyc.TextBrush"] = "#FF1F2329", ["Dyc.SubtleTextBrush"] = "#FF5F6874", ["Dyc.AccentBrush"] = "#FF0070C0",
            ["Dyc.SelectionBorderBrush"] = "#990070C0", ["Dyc.WireBrush"] = "#FF8A93A0", ["Dyc.WarningBrush"] = "#FFB26B00",
            ["Dyc.ErrorBrush"] = "#FFD33A3A", ["Dyc.InputBackgroundBrush"] = "#FFFFFFFF", ["Dyc.InputBorderBrush"] = "#FFB9C1CB",
            ["Dyc.HoverBrush"] = "#FFE6F0FA", ["Dyc.NoteBrush"] = "#FFFFF7E6", ["Dyc.NoteBorderBrush"] = "#FFFFE1A8", ["Dyc.SocketOutlineBrush"] = "#FF7C8896",
            ["Dyc.OverlayBrush"] = "#F5F7F8FA", ["Dyc.MinimapBrush"] = "#E6F7F8FA", ["Dyc.MinimapItemBrush"] = "#FF7A8594",
            ["Dyc.ViewportFillBrush"] = "#260070C0",
            ["Dyc.BrandStartColor"] = "#FFFFFFFF", ["Dyc.BrandEndColor"] = "#FFEDF1F6", ["Dyc.OnBrandBrush"] = "#FF1F2329",
            ["Dyc.OnBrandSubtleBrush"] = "#FF5F6874", ["Dyc.BrandButtonBrush"] = "#12000000", ["Dyc.BrandButtonHoverBrush"] = "#24000000",
            ["Dyc.FnCreateBrush"] = "#FF2E7D32", ["Dyc.FnModifyBrush"] = "#FFB26B00", ["Dyc.FnInfoBrush"] = "#FF0070C0",
            ["Dyc.PrimaryBrush"] = "#FF0070C0", ["Dyc.OnPrimaryBrush"] = "#FFFFFFFF",
        }),
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

    private static UiPalette Make(string id, string name, Dictionary<string, string> hex)
    {
        var map = new Dictionary<string, Color>(Keys.Count, StringComparer.Ordinal);
        foreach (var key in Keys)
        {
            if (!hex.TryGetValue(key, out var value))
            {
                throw new ArgumentException("Palette '" + id + "' does not define '" + key + "'.");
            }

            map[key] = (Color)ColorConverter.ConvertFromString(value);
        }

        foreach (var key in hex.Keys)
        {
            if (!map.ContainsKey(key))
            {
                throw new ArgumentException("Palette '" + id + "' defines the unknown key '" + key + "'.");
            }
        }

        return new UiPalette(id, name, map);
    }
}
