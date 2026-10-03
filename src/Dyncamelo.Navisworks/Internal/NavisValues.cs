using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Types;
using Dyncamelo.Nodes;
using Dyncamelo.Nodes.Portable;
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
            // A long that fits Int32 stays integral; anything bigger widens to
            // double (never a raw OverflowException on e.g. large IDs).
            case long l:
                return l >= int.MinValue && l <= int.MaxValue
                    ? VariantData.FromInt32((int)l)
                    : VariantData.FromDouble(l);
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
    /// Navisworks Color, a Dyncamelo <see cref="DyncameloColor"/> (Color.ByARGB,
    /// Color Picker — alpha is dropped, transparency is a separate override), a
    /// <see cref="System.Drawing.Color"/>, a hex string ("#RRGGBB" or "RRGGBB"),
    /// a list of three numbers (0-255, or 0.0-1.0 when every component is at
    /// most 1), or any type with a registered TypeCoercion converter to
    /// <see cref="NwColor"/>.
    /// </summary>
    internal static NwColor ToNavisColor(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No color provided.");
            case NwColor navisColor:
                return navisColor;
            case DyncameloColor dyncameloColor:
                return NwColor.FromByteRGB(dyncameloColor.R, dyncameloColor.G, dyncameloColor.B);
            case System.Drawing.Color drawingColor:
                return NwColor.FromByteRGB(drawingColor.R, drawingColor.G, drawingColor.B);
            case string text:
                return ParseHexColor(text);
            case IList list when !(value is string):
                return FromComponentList(list);
            default:
                if (TypeCoercion.TryCoerce(value, typeof(NwColor), out var coerced) && coerced is NwColor converted)
                {
                    return converted;
                }

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

    /// <summary>
    /// Indexes the names of the <typeparamref name="T"/> items of a saved-item collection (no descent), so a node that
    /// makes many items in one folder can ask "is there one with this name, and where" with a dictionary lookup instead of
    /// <see cref="FindTopLevelIndex{T}"/> over the growing folder for every item. Items of other kinds (folders,
    /// animations) take their position but are never found by name, exactly as in <see cref="FindTopLevelIndex{T}"/>.
    /// </summary>
    internal static TopLevelNameIndex BuildNameIndex<T>(IEnumerable<SavedItem> items) where T : SavedItem
    {
        return new TopLevelNameIndex(NamesOf<T>(items));
    }

    private static IEnumerable<string?> NamesOf<T>(IEnumerable<SavedItem> items) where T : SavedItem
    {
        foreach (var item in items)
        {
            yield return item is T ? item.DisplayName : null;
        }
    }

    /// <summary>
    /// After an item was added to, or replaced in, a collection that <paramref name="names"/> indexes: the STORED instance.
    /// The index says where it should be, and one look at that position confirms it. When the collection is not what the
    /// index thought (Navisworks put the item elsewhere, or under another name) the collection is read again, indexed
    /// again, and searched the way <see cref="FindTopLevelIndex{T}"/> would.
    /// </summary>
    /// <param name="children">The collection as it is after the edit.</param>
    /// <param name="read">Reads the collection again, for the case that the position did not hold the item.</param>
    /// <param name="names">The index of the collection.</param>
    /// <param name="name">The item's name.</param>
    /// <param name="expectedIndex">Where the item should be.</param>
    /// <returns>The stored item, or null when the collection holds no <typeparamref name="T"/> with that name.</returns>
    internal static T? ConfirmStored<T>(
        SavedItemCollection children,
        Func<SavedItemCollection> read,
        TopLevelNameIndex names,
        string name,
        int expectedIndex) where T : SavedItem
    {
        if (expectedIndex >= 0 &&
            expectedIndex < children.Count &&
            children[expectedIndex] is T item &&
            string.Equals(item.DisplayName, name, StringComparison.Ordinal))
        {
            return item;
        }

        var fresh = read();
        names.Reset(NamesOf<T>(fresh));
        return names.TryGetIndex(name, out var index) ? fresh[index] as T : null;
    }

    /// <summary>
    /// Converts a port value to a Navisworks <see cref="Point3D"/>. Accepts a
    /// Navisworks Point3D, a Dyncamelo <see cref="DyncameloPoint"/>
    /// (Point.ByCoordinates), or a list of three numbers.
    /// </summary>
    internal static Point3D ToPoint3D(object? value)
    {
        switch (value)
        {
            case null:
                throw new ArgumentNullException(nameof(value), "No point provided.");
            case Point3D point:
                return point;
            case DyncameloPoint dyncameloPoint:
                return new Point3D(dyncameloPoint.X, dyncameloPoint.Y, dyncameloPoint.Z);
            case IList list when !(value is string):
                if (list.Count < 3)
                {
                    throw new ArgumentException("A point list needs three numeric components (x, y, z).");
                }

                return new Point3D(
                    Convert.ToDouble(list[0], CultureInfo.InvariantCulture),
                    Convert.ToDouble(list[1], CultureInfo.InvariantCulture),
                    Convert.ToDouble(list[2], CultureInfo.InvariantCulture));
            default:
                if (TypeCoercion.TryCoerce(value, typeof(Point3D), out var coerced) && coerced is Point3D converted)
                {
                    return converted;
                }

                throw new ArgumentException(
                    "Cannot interpret a value of type '" + value.GetType().Name +
                    "' as a point. Wire a Point (e.g. Point.ByCoordinates or ClashResult.Center) or a list of three numbers.");
        }
    }

    /// <summary>
    /// A human-readable selection-tree path for a model item, e.g.
    /// "file.nwc &gt; Level 1 &gt; Walls &gt; Basic Wall". Unnamed nodes fall back to
    /// their class display name; still-empty segments are skipped.
    /// </summary>
    internal static string ItemPath(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var segments = new List<string>();
        for (var current = item; current != null; current = current.Parent)
        {
            var name = current.DisplayName;
            if (string.IsNullOrEmpty(name))
            {
                name = current.ClassDisplayName;
            }

            if (!string.IsNullOrEmpty(name))
            {
                segments.Add(name);
            }
        }

        segments.Reverse();
        return string.Join(" > ", segments);
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

    /// <summary>Null-checks a model item input.</summary>
    internal static ModelItem RequireItem(ModelItem? item)
    {
        return item ?? throw new ArgumentNullException(nameof(item), "No model item provided.");
    }

    /// <summary>Flattens a model-item input to a list and insists it has something in it.</summary>
    /// <param name="items">The wired items.</param>
    /// <param name="parameterName">The input's name, for the message.</param>
    internal static List<ModelItem> RequireItems(IEnumerable<ModelItem>? items, string parameterName = "items")
    {
        var list = ToItemList(items);
        if (list.Count == 0)
        {
            throw new ArgumentException("No model items provided for '" + parameterName + "'.", parameterName);
        }

        return list;
    }

    /// <summary>Flattens a model-item input to a list; an empty list is fine (the node then does nothing) but a missing input is an error.</summary>
    /// <param name="items">The wired items.</param>
    internal static List<ModelItem> NonNullItems(IEnumerable<ModelItem>? items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        return ToItemList(items);
    }

    /// <summary>A stable identity for one item: its InstanceGuid when the source format provides one, otherwise its selection-tree path.</summary>
    internal static string ItemIdentity(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var guid = item.InstanceGuid;
        return guid != Guid.Empty
            ? "guid:" + guid.ToString("N")
            : "path:" + ItemPath(item);
    }

    /// <summary>Creates the folder a file is about to be written into.</summary>
    internal static void EnsureDirectory(string filePath)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Scale from a named unit onto document units ("document" or empty = 1); <paramref name="unitsLabel"/> names the unit used.</summary>
    internal static double ResolveUnitsScale(Document doc, string? units, out string unitsLabel)
    {
        var trimmed = (units ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed.Equals("document", StringComparison.OrdinalIgnoreCase))
        {
            unitsLabel = doc.Units.ToString();
            return 1.0;
        }

        if (!Enum.TryParse<Units>(trimmed, true, out var parsed))
        {
            throw new ArgumentException(
                "Unknown units '" + units + "'. Use \"document\" or a Navisworks unit name " +
                "(e.g. \"Meters\", \"Millimeters\", \"Feet\" — Units.All lists them).", nameof(units));
        }

        unitsLabel = parsed.ToString();
        return UnitConversion.ScaleFactor(parsed, doc.Units);
    }

    /// <summary>Scale from a named unit onto document units ("document" or empty = 1).</summary>
    internal static double ResolveUnitsScale(Document doc, string? units) => ResolveUnitsScale(doc, units, out _);

    /// <summary>A comment status from its name (empty = New).</summary>
    internal static CommentStatus ParseCommentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return CommentStatus.New;
        }

        switch (status!.Trim().ToLowerInvariant())
        {
            case "new": return CommentStatus.New;
            case "active": return CommentStatus.Active;
            case "approved": return CommentStatus.Approved;
            case "resolved": return CommentStatus.Resolved;
            default:
                throw new ArgumentException(
                    "Unknown comment status '" + status + "'. Use \"New\", \"Active\", \"Approved\" or \"Resolved\".",
                    nameof(status));
        }
    }

    /// <summary>A value as the text of a grouping key: invariant culture, "(none)" for null and "(empty)" for empty text.</summary>
    internal static string FormatKey(object? value)
    {
        if (value == null)
        {
            return "(none)";
        }

        var text = value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString();
        return string.IsNullOrEmpty(text) ? "(empty)" : text!;
    }
}
