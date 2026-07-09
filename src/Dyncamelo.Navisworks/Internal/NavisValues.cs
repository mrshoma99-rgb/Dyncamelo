using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace Dyncamelo.Navisworks.Internal;

/// <summary>
/// Boundary conversions between raw Navisworks API values and plain .NET values
/// used on Dyncamelo ports. Internal — never surfaced as nodes.
/// </summary>
internal static class NavisValues
{
    /// <summary>
    /// Converts a <see cref="VariantData"/> to a plain CLR value. Never throws on
    /// a well-formed variant: the conversion is selected by <see cref="VariantData.DataType"/>.
    /// </summary>
    internal static object? ToClrObject(VariantData? variant)
    {
        if (variant == null)
        {
            return null;
        }

        switch (variant.DataType)
        {
            case VariantDataType.None: return null;
            case VariantDataType.Boolean: return variant.ToBoolean();
            case VariantDataType.Int32: return variant.ToInt32();
            case VariantDataType.Double: return variant.ToDouble();
            case VariantDataType.DoubleLength: return variant.ToDoubleLength();   // document units
            case VariantDataType.DoubleArea: return variant.ToDoubleArea();
            case VariantDataType.DoubleVolume: return variant.ToDoubleVolume();
            case VariantDataType.DoubleAngle: return variant.ToDoubleAngle();     // radians
            case VariantDataType.DateTime: return variant.ToDateTime();
            case VariantDataType.DisplayString: return variant.ToDisplayString();
            case VariantDataType.IdentifierString: return variant.ToIdentifierString();
            case VariantDataType.NamedConstant: return variant.ToNamedConstant()?.DisplayName;
            case VariantDataType.Point2D: return variant.ToPoint2D();
            case VariantDataType.Point3D: return variant.ToPoint3D();
            default: return variant.ToString();
        }
    }

    /// <summary>
    /// Builds a <see cref="VariantData"/> search value from a plain .NET value
    /// (string, bool, integral, floating point or DateTime).
    /// </summary>
    internal static VariantData ToVariant(object? value)
    {
        if (value == null)
        {
            return VariantData.FromNone();
        }

        switch (value)
        {
            case VariantData variant: return variant;
            case string text: return VariantData.FromDisplayString(text);
            case bool flag: return VariantData.FromBoolean(flag);
            case int i: return VariantData.FromInt32(i);
            case long l: return VariantData.FromInt32(checked((int)l));
            case double d: return VariantData.FromDouble(d);
            case float f: return VariantData.FromDouble(f);
            case decimal m: return VariantData.FromDouble((double)m);
            case DateTime time: return VariantData.FromDateTime(time);
            default:
                return VariantData.FromDisplayString(
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>
    /// Converts a port value to a Navisworks <see cref="NwColor"/>. Accepts a
    /// Navisworks Color, a <see cref="System.Drawing.Color"/>, a hex string
    /// ("#RRGGBB" or "RRGGBB"), or a list of three numbers (0-255, or 0.0-1.0
    /// when every component is at most 1).
    /// </summary>
    internal static NwColor ToNavisColor(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No color provided.");
            case NwColor navisColor:
                return navisColor;
            case System.Drawing.Color drawingColor:
                return NwColor.FromByteRGB(drawingColor.R, drawingColor.G, drawingColor.B);
            case string text:
                return ParseHexColor(text);
            case IList list when !(value is string):
                return FromComponentList(list);
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as a color. Wire a Color, a \"#RRGGBB\" string, or a list of three numbers.");
        }
    }

    /// <summary>Materializes any sequence of model items into a <see cref="List{ModelItem}"/>.</summary>
    internal static List<ModelItem> ToItemList(IEnumerable<ModelItem>? items)
    {
        var list = new List<ModelItem>();
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }
        }

        return list;
    }

    /// <summary>Materializes model items into a <see cref="ModelItemCollection"/> for API calls that require one.</summary>
    internal static ModelItemCollection ToItemCollection(IEnumerable<ModelItem>? items)
    {
        var collection = new ModelItemCollection();
        if (items != null)
        {
            collection.AddRange(ToItemList(items));
        }

        return collection;
    }

    /// <summary>
    /// Recursively collects every <typeparamref name="T"/> in a saved-item tree
    /// (folders/groups are descended into, other item kinds are skipped).
    /// Saved viewpoint animations are treated as leaves: their children are
    /// keyframes/cuts, not standalone saved items.
    /// </summary>
    internal static List<T> FlattenSavedItems<T>(IEnumerable<SavedItem> items) where T : SavedItem
    {
        var results = new List<T>();
        CollectSavedItems(items, results);
        return results;
    }

    private static void CollectSavedItems<T>(IEnumerable<SavedItem> items, List<T> results) where T : SavedItem
    {
        foreach (var item in items)
        {
            if (item is T match)
            {
                results.Add(match);
            }

            // Do not descend into the match itself when it is a group-shaped leaf
            // (e.g. a ClashTest whose children are results, or a TimelinerTask whose
            // children are subtasks handled by the caller's own recursion policy).
            if (!(item is T) && item is GroupItem group && !(item is SavedViewpointAnimation))
            {
                CollectSavedItems(group.Children, results);
            }
        }
    }

    /// <summary>
    /// Finds the first saved item of type <typeparamref name="T"/> with the given
    /// display name, or null. Saved viewpoint animations are not descended into
    /// (their children are keyframes, not standalone saved items).
    /// </summary>
    internal static T? FindSavedItemByName<T>(IEnumerable<SavedItem> items, string displayName) where T : SavedItem
    {
        foreach (var item in items)
        {
            if (item is T match && string.Equals(match.DisplayName, displayName, StringComparison.Ordinal))
            {
                return match;
            }

            if (item is GroupItem group && !(item is SavedViewpointAnimation))
            {
                var found = FindSavedItemByName<T>(group.Children, displayName);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Index of the first item of type <typeparamref name="T"/> with the given
    /// display name in a saved-item collection (no descent), or -1. Unlike
    /// <c>SavedItemCollection.IndexOfDisplayName</c> this never matches a
    /// same-named item of another kind (e.g. a folder or an animation), so
    /// replace-by-name operations cannot destroy an unrelated container.
    /// </summary>
    internal static int FindTopLevelIndex<T>(IEnumerable<SavedItem> items, string displayName) where T : SavedItem
    {
        int index = 0;
        foreach (var item in items)
        {
            if (item is T && string.Equals(item.DisplayName, displayName, StringComparison.Ordinal))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static NwColor ParseHexColor(string text)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex.Substring(2); // drop alpha
        }

        if (hex.Length != 6 ||
            !byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            throw new ArgumentException("'" + text + "' is not a valid \"#RRGGBB\" color string.");
        }

        return NwColor.FromByteRGB(r, g, b);
    }

    private static NwColor FromComponentList(IList list)
    {
        if (list.Count < 3)
        {
            throw new ArgumentException("A color list needs three numeric components (red, green, blue).");
        }

        var components = new double[3];
        for (int i = 0; i < 3; i++)
        {
            components[i] = Convert.ToDouble(list[i], CultureInfo.InvariantCulture);
        }

        // Heuristic: components all within [0,1] are unit doubles; otherwise bytes.
        if (components[0] <= 1.0 && components[1] <= 1.0 && components[2] <= 1.0)
        {
            return new NwColor(components[0], components[1], components[2]);
        }

        return NwColor.FromByteRGB(
            (byte)Math.Max(0, Math.Min(255, components[0])),
            (byte)Math.Max(0, Math.Min(255, components[1])),
            (byte)Math.Max(0, Math.Min(255, components[2])));
    }
}
