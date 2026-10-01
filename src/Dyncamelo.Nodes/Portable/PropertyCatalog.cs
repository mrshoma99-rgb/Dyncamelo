using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// Accumulates "what data does this model actually have?" for Properties.Discover: for every
/// (category, property) pair how many items carry it, how many distinct values it holds (counted up to a cap, by
/// 64-bit hash so a big model does not keep every string) and the first few distinct values as samples.
/// Pure (no Navisworks types), so the counting rules are unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class PropertyCatalog
{
    /// <summary>Distinct values are counted up to this many per property.</summary>
    public const int DefaultDistinctCap = 10000;

    /// <summary>A sample longer than this is cut and ends with "...".</summary>
    public const int MaxSampleLength = 100;

    private readonly int _maxSamples;
    private readonly int _distinctCap;
    private readonly Dictionary<string, Dictionary<string, Entry>> _byCategory =
        new Dictionary<string, Dictionary<string, Entry>>(StringComparer.Ordinal);

    /// <summary>Creates an empty catalog.</summary>
    /// <param name="maxSamples">How many distinct sample values to keep per property (0 keeps none).</param>
    /// <param name="distinctCap">Distinct values are counted up to this many per property.</param>
    public PropertyCatalog(int maxSamples, int distinctCap = DefaultDistinctCap)
    {
        if (maxSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSamples), "maxSamples cannot be negative.");
        }

        if (distinctCap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(distinctCap), "distinctCap must be at least 1.");
        }

        _maxSamples = maxSamples;
        _distinctCap = distinctCap;
    }

    /// <summary>Number of distinct (category, property) pairs seen so far.</summary>
    public int PropertyCount
    {
        get
        {
            var count = 0;
            foreach (var properties in _byCategory.Values)
            {
                count += properties.Count;
            }

            return count;
        }
    }

    /// <summary>
    /// Records one property of one item. An item that lists the same property twice counts once in Items; its
    /// values still count as values. Empty values (null or "") count as "has the property" but not as a value.
    /// </summary>
    /// <param name="itemIndex">A number that is the same for all properties of one item and different for each item.</param>
    /// <param name="category">The category display name.</param>
    /// <param name="property">The property display name.</param>
    /// <param name="value">The property value.</param>
    public void Add(int itemIndex, string? category, string? property, object? value)
    {
        var categoryName = category ?? string.Empty;
        var propertyName = property ?? string.Empty;
        if (!_byCategory.TryGetValue(categoryName, out var properties))
        {
            properties = new Dictionary<string, Entry>(StringComparer.Ordinal);
            _byCategory[categoryName] = properties;
        }

        if (!properties.TryGetValue(propertyName, out var entry))
        {
            entry = new Entry();
            properties[propertyName] = entry;
        }

        if (entry.LastItem != itemIndex)
        {
            entry.LastItem = itemIndex;
            entry.Items++;
        }

        var text = ValueText(value);
        if (text.Length == 0 || entry.Hashes.Count >= _distinctCap)
        {
            return;
        }

        if (entry.Hashes.Add(Hash(text)) && entry.Samples.Count < _maxSamples)
        {
            entry.Samples.Add(text.Length > MaxSampleLength ? text.Substring(0, MaxSampleLength - 3) + "..." : text);
        }
    }

    /// <summary>
    /// The catalog as a table with the columns Category, Property, Items, Distinct and Samples, one row per
    /// (category, property), ordered by category and then property (case-insensitive).
    /// </summary>
    /// <returns>The table.</returns>
    public DyncameloTable ToTable()
    {
        var categories = new List<string>(_byCategory.Keys);
        categories.Sort(CompareNames);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var category in categories)
        {
            var properties = _byCategory[category];
            var names = new List<string>(properties.Keys);
            names.Sort(CompareNames);
            foreach (var name in names)
            {
                var entry = properties[name];
                rows.Add(new object?[]
                {
                    category,
                    name,
                    (double)entry.Items,
                    (double)entry.Hashes.Count,
                    string.Join(" | ", entry.Samples),
                });
            }
        }

        return new DyncameloTable(new[] { "Category", "Property", "Items", "Distinct", "Samples" }, rows);
    }

    /// <summary>A property value as the invariant text used for counting and sampling ("" for null).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string ValueText(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is DateTime date)
        {
            return date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return TypeCoercion.FormatValue(value);
    }

    private static int CompareNames(string a, string b)
    {
        var result = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return result != 0 ? result : string.CompareOrdinal(a, b);
    }

    /// <summary>FNV-1a, 64 bit: a value is remembered as this hash, not as the text.</summary>
    private static long Hash(string text)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }

            return (long)hash;
        }
    }

    private sealed class Entry
    {
        internal int Items;
        internal int LastItem = -1;
        internal readonly HashSet<long> Hashes = new HashSet<long>();
        internal readonly List<string> Samples = new List<string>();
    }
}
