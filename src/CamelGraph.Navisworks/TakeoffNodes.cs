using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Navisworks.Api;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;

namespace CamelGraph.Navisworks;

/// <summary>Quantity take-off rollup nodes.</summary>
[NodeCategory("Navisworks.Analysis")]
public static class TakeoffNodes
{
    /// <summary>Groups items by one property and sums another per group.</summary>
    /// <param name="items">The model items to roll up.</param>
    /// <param name="groupCategoryName">Category of the grouping property (e.g. "Element").</param>
    /// <param name="groupPropertyName">Grouping property (e.g. "Level" or "System Type").</param>
    /// <param name="valueCategoryName">Category of the quantity property (e.g. "Element").</param>
    /// <param name="valuePropertyName">Numeric quantity property to sum (e.g. "Volume", "Length", "Area").</param>
    /// <returns>Index-aligned group keys, per-group sums and per-group item counts, and the same as one table.</returns>
    [NodeName("Takeoff.SumPropertyByGroup")]
    [NodeDescription(
        "One-node QTO rollup: groups items by a property value and sums a numeric property per group (e.g. Volume per Level). Items without the grouping property land in \"(none)\". " +
        "keys, sums and counts are three parallel lists; table is the same result as one table (columns: the grouping property, the summed property, Count) that goes straight into " +
        "Table.ToExcelFile, Table.ToCsvFile, Table.Sort or Report.Html. For sums over several properties use Properties.ToTable and Table.GroupBy.")]
    [NodeSearchTags("takeoff", "qto", "quantity", "sum", "group", "rollup", "pivot", "table")]
    [MultiReturn("keys", "sums", "counts", "table")]
    [PortKinds("text*", "number*", "integer*", "data")]
    public static Dictionary<string, object?> SumPropertyByGroup(
        [MultiInput] IEnumerable<ModelItem> items,
        [NodeTabChoice("items")] string groupCategoryName,
        [NodePropertyChoice("items", "groupCategoryName")] string groupPropertyName,
        [NodeTabChoice("items")] string valueCategoryName,
        [NodePropertyChoice("items", "valueCategoryName")] string valuePropertyName)
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

        var rows = new List<IReadOnlyList<object?>>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            rows.Add(new object?[] { keys[i], sums[i], counts[i] });
        }

        return new Dictionary<string, object?>
        {
            ["keys"] = keys,
            ["sums"] = sums,
            ["counts"] = counts,
            ["table"] = new CamelGraphTable(new[] { groupPropertyName, valuePropertyName, "Count" }, rows),
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
