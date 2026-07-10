using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes that override how model items look in the viewport.</summary>
[NodeCategory("Navisworks.Appearance")]
public static class AppearanceNodes
{
    /// <summary>Overrides the color of model items.</summary>
    /// <param name="items">The model items to recolor.</param>
    /// <param name="color">A Color, a "#RRGGBB" string, or a list of three numbers.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The recolored items (pass-through).</returns>
    [NodeName("Appearance.OverrideColor")]
    [NodeDescription("Overrides the color of model items (a permanent override: saved with the file and undoable).")]
    [NodeSearchTags("appearance", "color", "override", "paint", "tint")]
    [return: NodeName("items")]
    public static List<ModelItem> OverrideColor(IEnumerable<ModelItem> items, object color, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentColor(list, NavisValues.ToNavisColor(color));
        return list;
    }

    /// <summary>Overrides the transparency of model items.</summary>
    /// <param name="items">The model items to change.</param>
    /// <param name="transparency">0 = fully opaque, 1 = fully transparent.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The changed items (pass-through).</returns>
    [NodeName("Appearance.OverrideTransparency")]
    [NodeDescription("Overrides the transparency of model items (0 = opaque, 1 = invisible).")]
    [NodeSearchTags("appearance", "transparency", "override", "ghost", "opacity")]
    [return: NodeName("items")]
    public static List<ModelItem> OverrideTransparency(
        IEnumerable<ModelItem> items,
        double transparency,
        Document? document = null)
    {
        if (transparency < 0.0 || transparency > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transparency), "Transparency must be between 0 (opaque) and 1 (invisible).");
        }

        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.OverridePermanentTransparency(list, transparency);
        return list;
    }

    /// <summary>Removes color/transparency overrides from model items.</summary>
    /// <param name="items">The model items to reset.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The reset items (pass-through).</returns>
    [NodeName("Appearance.Reset")]
    [NodeDescription("Removes permanent color and transparency overrides from model items, restoring their original materials.")]
    [NodeSearchTags("appearance", "reset", "restore", "original", "materials")]
    [return: NodeName("items")]
    public static List<ModelItem> Reset(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.ResetPermanentMaterials(list);
        return list;
    }

    /// <summary>Hides model items in the viewport.</summary>
    /// <param name="items">The model items to hide.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The hidden items (pass-through).</returns>
    [NodeName("Appearance.Hide")]
    [NodeDescription("Hides model items in the viewport.")]
    [NodeSearchTags("appearance", "hide", "hidden", "invisible")]
    [return: NodeName("items")]
    public static List<ModelItem> Hide(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.SetHidden(list, true);
        return list;
    }

    /// <summary>Shows (un-hides) model items in the viewport.</summary>
    /// <param name="items">The model items to show.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The shown items (pass-through).</returns>
    [NodeName("Appearance.Show")]
    [NodeDescription("Shows (un-hides) model items in the viewport.")]
    [NodeSearchTags("appearance", "show", "unhide", "visible")]
    [return: NodeName("items")]
    public static List<ModelItem> Show(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.SetHidden(list, false);
        return list;
    }

    /// <summary>Removes every appearance override in the model.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>True when the overrides were cleared.</returns>
    [NodeName("Appearance.ResetAll")]
    [NodeDescription("Removes every permanent color/transparency override in the model — a clean slate before re-coloring.")]
    [NodeSearchTags("appearance", "reset", "all", "clear", "clean")]
    [return: NodeName("done")]
    public static bool ResetAll(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        doc.Models.ResetAllPermanentMaterials();
        return true;
    }

    /// <summary>Shows only the given items; hides everything else.</summary>
    /// <param name="items">The model items to isolate.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The isolated items (pass-through).</returns>
    [NodeName("Appearance.Isolate")]
    [NodeDescription("Shows only these items and hides everything else (undo with Appearance.Show on Models.RootItems).")]
    [NodeSearchTags("appearance", "isolate", "only", "hide", "focus")]
    [return: NodeName("items")]
    public static List<ModelItem> Isolate(IEnumerable<ModelItem> items, Document? document = null)
    {
        var list = RequireItems(items);
        var doc = NavisworksContext.ResolveDocument(document);

        var inverted = NavisValues.ToItemCollection(list);
        inverted.Invert(doc); // in place: now everything EXCEPT the items
        doc.Models.SetHidden(inverted, true);
        doc.Models.SetHidden(list, false);
        return list;
    }

    /// <summary>Color-codes items by their values and returns the value→color legend.</summary>
    /// <param name="items">The model items to color.</param>
    /// <param name="values">One value per item (same length as items) — the color key.</param>
    /// <param name="palette">Colors to cycle through per distinct value (null uses a built-in palette; all-numeric values get a blue→red gradient instead).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The colored items and a value → "#RRGGBB" legend for reporting.</returns>
    [NodeName("Appearance.ColorByValues")]
    [NodeDescription("One-node color-coding: pairs each item with its value, colors each distinct value (categorical palette, or a blue→red gradient when every value is numeric) and outputs the legend.")]
    [NodeSearchTags("appearance", "color", "values", "legend", "heatmap", "code", "byvalue")]
    [MultiReturn("items", "legend")]
    public static Dictionary<string, object?> ColorByValues(
        IEnumerable<ModelItem> items,
        IEnumerable<object?> values,
        IEnumerable<object>? palette = null,
        Document? document = null)
    {
        var itemList = RequireItems(items);
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "No values provided.");
        }

        var valueList = new List<object?>(values);
        if (valueList.Count != itemList.Count)
        {
            throw new ArgumentException(
                "Got " + itemList.Count + " items but " + valueList.Count +
                " values — wire one value per item (e.g. from Properties.Value with lacing).", nameof(values));
        }

        var doc = NavisworksContext.ResolveDocument(document);

        // Bucket item indices per distinct value key, keeping first-seen order.
        var keys = new List<string>();
        var buckets = new Dictionary<string, List<ModelItem>>(StringComparer.Ordinal);
        var allNumeric = valueList.Count > 0;
        for (int i = 0; i < itemList.Count; i++)
        {
            var key = FormatKey(valueList[i]);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<ModelItem>();
                buckets[key] = bucket;
                keys.Add(key);
            }

            bucket.Add(itemList[i]);
            allNumeric &= IsNumeric(valueList[i]);
        }

        var colors = AssignColors(keys, palette, allNumeric);
        var legend = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            var color = colors[i];
            doc.Models.OverridePermanentColor(buckets[keys[i]], color);
            legend[keys[i]] = ToHex(color);
        }

        return new Dictionary<string, object?>
        {
            ["items"] = itemList,
            ["legend"] = legend,
        };
    }

    private static string FormatKey(object? value)
    {
        if (value == null)
        {
            return "(none)";
        }

        var text = value is IFormattable formattable
            ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString();
        return string.IsNullOrEmpty(text) ? "(empty)" : text!;
    }

    private static bool IsNumeric(object? value)
    {
        return value is double || value is float || value is decimal ||
               value is int || value is long || value is short || value is byte;
    }

    private static List<Autodesk.Navisworks.Api.Color> AssignColors(
        List<string> keys,
        IEnumerable<object>? palette,
        bool allNumeric)
    {
        var colors = new List<Autodesk.Navisworks.Api.Color>(keys.Count);
        if (palette != null)
        {
            var paletteColors = new List<Autodesk.Navisworks.Api.Color>();
            foreach (var entry in palette)
            {
                paletteColors.Add(NavisValues.ToNavisColor(entry));
            }

            if (paletteColors.Count == 0)
            {
                throw new ArgumentException("The palette is empty. Wire at least one color, or leave it unconnected.", nameof(palette));
            }

            for (int i = 0; i < keys.Count; i++)
            {
                colors.Add(paletteColors[i % paletteColors.Count]);
            }

            return colors;
        }

        if (allNumeric && keys.Count > 1)
        {
            // Numeric values: blue → red gradient over the keys sorted numerically.
            var sorted = new List<string>(keys);
            sorted.Sort((a, b) => double.Parse(a, System.Globalization.CultureInfo.InvariantCulture)
                .CompareTo(double.Parse(b, System.Globalization.CultureInfo.InvariantCulture)));
            var rankByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < sorted.Count; i++)
            {
                rankByKey[sorted[i]] = i;
            }

            foreach (var key in keys)
            {
                var t = rankByKey[key] / (double)(sorted.Count - 1);
                colors.Add(new Autodesk.Navisworks.Api.Color(t, 0.1, 1.0 - t));
            }

            return colors;
        }

        for (int i = 0; i < keys.Count; i++)
        {
            colors.Add(DefaultPalette[i % DefaultPalette.Length]);
        }

        return colors;
    }

    private static string ToHex(Autodesk.Navisworks.Api.Color color)
    {
        return "#" +
            ((int)Math.Round(color.R * 255.0)).ToString("X2") +
            ((int)Math.Round(color.G * 255.0)).ToString("X2") +
            ((int)Math.Round(color.B * 255.0)).ToString("X2");
    }

    /// <summary>Twelve well-separated categorical colors (colorblind-conscious ordering).</summary>
    private static readonly Autodesk.Navisworks.Api.Color[] DefaultPalette =
    {
        Autodesk.Navisworks.Api.Color.FromByteRGB(31, 119, 180),   // blue
        Autodesk.Navisworks.Api.Color.FromByteRGB(255, 127, 14),   // orange
        Autodesk.Navisworks.Api.Color.FromByteRGB(44, 160, 44),    // green
        Autodesk.Navisworks.Api.Color.FromByteRGB(214, 39, 40),    // red
        Autodesk.Navisworks.Api.Color.FromByteRGB(148, 103, 189),  // purple
        Autodesk.Navisworks.Api.Color.FromByteRGB(140, 86, 75),    // brown
        Autodesk.Navisworks.Api.Color.FromByteRGB(227, 119, 194),  // pink
        Autodesk.Navisworks.Api.Color.FromByteRGB(127, 127, 127),  // gray
        Autodesk.Navisworks.Api.Color.FromByteRGB(188, 189, 34),   // olive
        Autodesk.Navisworks.Api.Color.FromByteRGB(23, 190, 207),   // cyan
        Autodesk.Navisworks.Api.Color.FromByteRGB(255, 187, 120),  // light orange
        Autodesk.Navisworks.Api.Color.FromByteRGB(152, 223, 138),  // light green
    };

    private static List<ModelItem> RequireItems(IEnumerable<ModelItem>? items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        return NavisValues.ToItemList(items);
    }
}
