using System;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;

namespace CamelGraph.Nodes.Portable;

/// <summary>
/// The distinct values of a property as groups, for SelectionSets.BulkByPropertyValues. Values are grouped by the text they show, but the
/// same visible value is often stored as text in one file and as a number in another, and Navisworks matches a value only when the data
/// type is the same. So a group keeps EVERY storage variant seen for its text (once each), and the set built for the group searches for
/// all of them; keeping only the first one seen left out the items stored the other way. Pure (no Navisworks types), so it is unit-tested.
/// </summary>
/// <typeparam name="T">The type of a storage variant (a Navisworks VariantData in the node).</typeparam>
[IsVisibleInLibrary(false)]
public sealed class DistinctValueGroups<T>
{
    /// <summary>The key of an item whose property has no value.</summary>
    public const string NoneKey = "(none)";

    /// <summary>The key of an item whose property is empty text.</summary>
    public const string EmptyKey = "(empty)";

    private readonly Dictionary<string, List<T>> _variants = new Dictionary<string, List<T>>(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    private readonly List<string> _keys = new List<string>();

    /// <summary>How many distinct texts there are.</summary>
    public int Count => _keys.Count;

    /// <summary>Records one occurrence of a value.</summary>
    /// <param name="key">The text of the value (the name of its set).</param>
    /// <param name="variant">How it is stored.</param>
    /// <param name="variantKey">Tells two storage variants apart: equal text means the same variant (the data type plus the value).</param>
    public void Add(string key, T variant, string variantKey)
    {
        if (!_variants.TryGetValue(key, out var list))
        {
            list = new List<T>();
            _variants[key] = list;
            _seen[key] = new HashSet<string>(StringComparer.Ordinal);
            _keys.Add(key);
        }

        if (_seen[key].Add(variantKey))
        {
            list.Add(variant);
        }
    }

    /// <summary>Every storage variant seen for a text, in the order first seen.</summary>
    /// <param name="key">The text.</param>
    public IReadOnlyList<T> VariantsOf(string key) => _variants[key];

    /// <summary>The texts in the order their sets are made: see <see cref="Order"/>.</summary>
    public List<string> SortedKeys() => Order(_keys);

    /// <summary>
    /// Puts value texts in order: numerically (smallest first) when every text but the two special ones is a number, so "2" comes before
    /// "10"; otherwise alphabetically, ignoring case. "(none)" and "(empty)" come last either way.
    /// </summary>
    /// <param name="keys">The texts.</param>
    /// <returns>A new, ordered list.</returns>
    public static List<string> Order(IEnumerable<string> keys)
    {
        var ordinary = new List<string>();
        var special = new List<string>();
        foreach (var key in keys)
        {
            if (string.Equals(key, NoneKey, StringComparison.Ordinal) || string.Equals(key, EmptyKey, StringComparison.Ordinal))
            {
                special.Add(key);
            }
            else
            {
                ordinary.Add(key);
            }
        }

        var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
        var allNumeric = ordinary.Count > 0;
        foreach (var key in ordinary)
        {
            if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number))
            {
                numbers[key] = number;
            }
            else
            {
                allNumeric = false;
                break;
            }
        }

        if (allNumeric)
        {
            ordinary.Sort((a, b) =>
            {
                var byNumber = numbers[a].CompareTo(numbers[b]);
                return byNumber != 0 ? byNumber : string.CompareOrdinal(a, b);
            });
        }
        else
        {
            ordinary.Sort((a, b) =>
            {
                var byText = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return byText != 0 ? byText : string.CompareOrdinal(a, b);
            });
        }

        special.Sort(string.CompareOrdinal);
        special.Reverse(); // "(none)" sorts after "(empty)" ordinally; the order wanted is (none) then (empty)
        ordinary.AddRange(special);
        return ordinary;
    }
}
