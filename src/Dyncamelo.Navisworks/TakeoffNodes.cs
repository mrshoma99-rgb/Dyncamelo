using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Quantity take-off rollup nodes.</summary>
[NodeCategory("Navisworks.Takeoff")]
public static class TakeoffNodes
{
    /// <summary>Groups items by one property and sums another per group.</summary>
    /// <param name="items">The model items to roll up.</param>
    /// <param name="groupCategoryName">Category of the grouping property (e.g. "Element").</param>
    /// <param name="groupPropertyName">Grouping property (e.g. "Level" or "System Type").</param>
    /// <param name="valueCategoryName">Category of the quantity property (e.g. "Element").</param>
    /// <param name="valuePropertyName">Numeric quantity property to sum (e.g. "Volume", "Length", "Area").</param>
    /// <returns>Index-aligned group keys, per-group sums and per-group item counts.</returns>
    [NodeName("Takeoff.SumPropertyByGroup")]
    [NodeDescription("One-node QTO rollup: groups items by a property value and sums a numeric property per group (e.g. Volume per Level). Items without the grouping property land in \"(none)\".")]
    [NodeSearchTags("takeoff", "qto", "quantity", "sum", "group", "rollup", "pivot")]
    [MultiReturn("keys", "sums", "counts")]
    public static Dictionary<string, object?> SumPropertyByGroup(
        IEnumerable<ModelItem> items,
        string groupCategoryName,
        string groupPropertyName,
        string valueCategoryName,
        string valuePropertyName)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items), "No model items provided.");
        }

        RequireName(groupCategoryName, nameof(groupCategoryName), "grouping property category");
        RequireName(groupPropertyName, nameof(groupPropertyName), "grouping property");
        RequireName(valueCategoryName, nameof(valueCategoryName), "quantity property category");
        RequireName(valuePropertyName, nameof(valuePropertyName), "quantity property");

        var keys = new List<string>();
        var sums = new List<double>();
        var counts = new List<int>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in NavisValues.ToItemList(items))
        {
            var key = ReadAsString(item, groupCategoryName, groupPropertyName) ?? "(none)";
            if (!indexByKey.TryGetValue(key, out var index))
            {
                index = keys.Count;
                indexByKey[key] = index;
                keys.Add(key);
                sums.Add(0.0);
                counts.Add(0);
            }

            sums[index] += ReadAsNumber(item, valueCategoryName, valuePropertyName);
            counts[index]++;
        }

        return new Dictionary<string, object?>
        {
            ["keys"] = keys,
            ["sums"] = sums,
            ["counts"] = counts,
        };
    }

    private static void RequireName(string value, string paramName, string what)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("No " + what + " name provided.", paramName);
        }
    }

    private static string? ReadAsString(ModelItem item, string categoryName, string propertyName)
    {
        var value = PropertyNodes.Value(item, categoryName, propertyName);
        if (value == null)
        {
            return null;
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString();
    }

    private static double ReadAsNumber(ModelItem item, string categoryName, string propertyName)
    {
        var value = PropertyNodes.Value(item, categoryName, propertyName);
        switch (value)
        {
            case null:
                return 0.0; // missing quantity contributes nothing but the item still counts
            case double d: return d;
            case int i: return i;
            case bool _:
                return 0.0;
            default:
                return double.TryParse(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0.0;
        }
    }
}
