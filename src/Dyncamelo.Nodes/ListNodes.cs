using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// List manipulation nodes. List parameters are declared as
/// <c>IList&lt;object&gt;</c> so incoming lists arrive whole (the engine never
/// replicates over them); scalar parameters such as indexes still replicate.
/// </summary>
[NodeCategory("List")]
public static class ListNodes
{
    /// <summary>
    /// Retrieves an element by position. Negative indexes count from the end
    /// (-1 is the last element), matching Dynamo behavior.
    /// </summary>
    /// <param name="list">The list to read from.</param>
    /// <param name="index">Zero-based index; negative values count from the end.</param>
    /// <returns>The element at the index.</returns>
    [NodeName("List.GetItemAtIndex")]
    [return: NodeName("item")]
    [NodeDescription("Returns the element at the given index (negative indexes count from the end).")]
    [NodeSearchTags("element", "at", "index", "pick")]
    public static object? GetItemAtIndex(IList<object?> list, int index)
    {
        RequireList(list, "List.GetItemAtIndex");
        var effective = index < 0 ? list.Count + index : index;
        if (effective < 0 || effective >= list.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Index " + index.ToString(CultureInfo.InvariantCulture) +
                " is out of range for a list of " + list.Count.ToString(CultureInfo.InvariantCulture) + " element(s).");
        }

        return list[effective];
    }

    /// <summary>Number of elements in a list.</summary>
    /// <param name="list">The list to count.</param>
    /// <returns>The element count.</returns>
    [NodeName("List.Count")]
    [return: NodeName("count")]
    [NodeDescription("Returns the number of elements in a list.")]
    [NodeSearchTags("length", "size")]
    public static int Count(IList<object?> list)
    {
        RequireList(list, "List.Count");
        return list.Count;
    }

    /// <summary>First element of a list.</summary>
    /// <param name="list">The list to read from (must not be empty).</param>
    /// <returns>The first element.</returns>
    [NodeName("List.FirstItem")]
    [return: NodeName("item")]
    [NodeDescription("Returns the first element of a list.")]
    [NodeSearchTags("head", "front")]
    public static object? FirstItem(IList<object?> list)
    {
        RequireList(list, "List.FirstItem");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.FirstItem requires a non-empty list.");
        }

        return list[0];
    }

    /// <summary>
    /// Flattens nested lists. By default (<paramref name="amount"/> = -1) all
    /// nesting is removed; a positive amount removes that many levels only.
    /// </summary>
    /// <param name="list">The (possibly nested) list to flatten.</param>
    /// <param name="amount">Levels of nesting to remove; -1 flattens completely.</param>
    /// <returns>The flattened list.</returns>
    [NodeName("List.Flatten")]
    [return: NodeName("list")]
    [NodeDescription("Flattens a nested list by a given number of levels (-1 = completely).")]
    [NodeSearchTags("nested", "unwrap")]
    public static IList<object?> Flatten(IList<object?> list, int amount = -1)
    {
        RequireList(list, "List.Flatten");
        var output = new List<object?>();
        FlattenInto(list, amount, output);
        return output;
    }

    /// <summary>
    /// Splits a list into two lists using a boolean mask of the same length:
    /// elements whose mask entry is true go to "in", the rest to "out".
    /// </summary>
    /// <param name="list">The list to filter.</param>
    /// <param name="mask">Booleans (or values coercible to booleans), one per element.</param>
    /// <returns>Dictionary with "in" and "out" lists.</returns>
    [NodeName("List.FilterByBoolMask")]
    [MultiReturn("in", "out")]
    [NodeDescription("Splits a list into elements whose mask entry is true (\"in\") and the rest (\"out\").")]
    [NodeSearchTags("filter", "mask", "partition", "sieve")]
    public static Dictionary<string, object> FilterByBoolMask(IList<object?> list, IList<object?> mask)
    {
        RequireList(list, "List.FilterByBoolMask");
        if (mask == null)
        {
            throw new ArgumentNullException(nameof(mask), "List.FilterByBoolMask requires a mask list.");
        }

        if (list.Count != mask.Count)
        {
            throw new ArgumentException(
                "List.FilterByBoolMask requires the list (" + list.Count.ToString(CultureInfo.InvariantCulture) +
                " element(s)) and the mask (" + mask.Count.ToString(CultureInfo.InvariantCulture) +
                " element(s)) to have the same length.");
        }

        var accepted = new List<object?>();
        var rejected = new List<object?>();
        for (int i = 0; i < list.Count; i++)
        {
            if (!TypeCoercion.TryCoerce(mask[i], typeof(bool), out var flag) || !(flag is bool))
            {
                throw new ArgumentException(
                    "Mask element at index " + i.ToString(CultureInfo.InvariantCulture) + " is not a boolean.");
            }

            if ((bool)flag)
            {
                accepted.Add(list[i]);
            }
            else
            {
                rejected.Add(list[i]);
            }
        }

        return new Dictionary<string, object>
        {
            ["in"] = accepted,
            ["out"] = rejected,
        };
    }

    /// <summary>
    /// Produces a numeric sequence from start towards end (inclusive, with a
    /// small tolerance for floating-point drift). A step moving away from end
    /// yields an empty list; a zero step is an error.
    /// </summary>
    /// <param name="start">First value of the sequence.</param>
    /// <param name="end">Inclusive upper (or lower, for negative steps) bound.</param>
    /// <param name="step">Increment between values; may be negative.</param>
    /// <returns>The sequence as a list of numbers.</returns>
    [NodeName("List.Range")]
    [return: NodeName("list")]
    [NodeDescription("Creates a sequence of numbers from start to end using the given step.")]
    [NodeSearchTags("sequence", "series", "numbers")]
    public static IList<double> Range(double start, double end, double step = 1d)
    {
        if (step == 0d)
        {
            throw new ArgumentException("List.Range requires a non-zero step.", nameof(step));
        }

        var result = new List<double>();
        var tolerance = Math.Abs(step) * 1e-9;
        if (step > 0d)
        {
            for (var value = start; value <= end + tolerance; value += step)
            {
                result.Add(value);
            }
        }
        else
        {
            for (var value = start; value >= end - tolerance; value += step)
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>
    /// Sorts a list ascending. Numbers sort numerically (regardless of numeric
    /// type), strings ordinally; mixed incomparable types raise an error.
    /// The sort is stable and the input list is not modified.
    /// </summary>
    /// <param name="list">The list to sort.</param>
    /// <returns>A new sorted list.</returns>
    [NodeName("List.Sort")]
    [return: NodeName("list")]
    [NodeDescription("Returns the list sorted ascending (numbers numerically, strings alphabetically).")]
    [NodeSearchTags("order", "ascending", "arrange")]
    public static IList<object?> Sort(IList<object?> list)
    {
        RequireList(list, "List.Sort");
        return list.OrderBy(item => item, NodeValueComparer.Instance).ToList();
    }

    /// <summary>
    /// Removes duplicate elements, keeping the first occurrence of each value.
    /// Numbers compare by value regardless of numeric type.
    /// </summary>
    /// <param name="list">The list to deduplicate.</param>
    /// <returns>A new list with duplicates removed, in original order.</returns>
    [NodeName("List.UniqueItems")]
    [return: NodeName("list")]
    [NodeDescription("Removes duplicate elements from a list, preserving the original order.")]
    [NodeSearchTags("distinct", "deduplicate", "unique")]
    public static IList<object?> UniqueItems(IList<object?> list)
    {
        RequireList(list, "List.UniqueItems");
        var seen = new HashSet<object?>(NodeValueEqualityComparer.Instance);
        var result = new List<object?>();
        foreach (var item in list)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>Last element of a list.</summary>
    /// <param name="list">The list to read from (must not be empty).</param>
    /// <returns>The last element.</returns>
    [NodeName("List.LastItem")]
    [return: NodeName("item")]
    [NodeDescription("Returns the last element of a list.")]
    [NodeSearchTags("tail", "end", "final")]
    public static object? LastItem(IList<object?> list)
    {
        RequireList(list, "List.LastItem");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.LastItem requires a non-empty list.");
        }

        return list[list.Count - 1];
    }

    /// <summary>
    /// Tests whether a list contains a value, using the same coercing equality
    /// as the Equals node (2 equals 2.0; strings compare ordinally).
    /// </summary>
    /// <param name="list">The list to search.</param>
    /// <param name="item">The value to look for.</param>
    /// <returns>True when the value occurs in the list.</returns>
    [NodeName("List.Contains")]
    [return: NodeName("contains")]
    [NodeDescription("Tests whether a list contains a value (numbers compare by value regardless of numeric type).")]
    [NodeSearchTags("membership", "includes", "has", "any")]
    public static bool Contains(IList<object?> list, object? item)
    {
        RequireList(list, "List.Contains");
        return IndexOfValue(list, item) >= 0;
    }

    /// <summary>
    /// Index of the first occurrence of a value in a list (coercing equality),
    /// or -1 when the value is absent.
    /// </summary>
    /// <param name="list">The list to search.</param>
    /// <param name="item">The value to look for.</param>
    /// <returns>The zero-based index, or -1 when not found.</returns>
    [NodeName("List.IndexOf")]
    [return: NodeName("index")]
    [NodeDescription("Returns the index of the first occurrence of a value in a list (-1 when absent).")]
    [NodeSearchTags("find", "position", "locate", "search")]
    public static int IndexOf(IList<object?> list, object? item)
    {
        RequireList(list, "List.IndexOf");
        return IndexOfValue(list, item);
    }

    /// <summary>Reverses the order of a list (the input list is not modified).</summary>
    /// <param name="list">The list to reverse.</param>
    /// <returns>A new list in reverse order.</returns>
    [NodeName("List.Reverse")]
    [return: NodeName("reversed")]
    [NodeDescription("Returns the list in reverse order.")]
    [NodeSearchTags("flip", "invert", "backwards")]
    public static IList<object?> Reverse(IList<object?> list)
    {
        RequireList(list, "List.Reverse");
        var result = new List<object?>(list);
        result.Reverse();
        return result;
    }

    /// <summary>Appends a value to the end of a list (returns a new list; the input is not modified).</summary>
    /// <param name="list">The list to append to.</param>
    /// <param name="item">The value to append.</param>
    /// <returns>A new list with the value appended.</returns>
    [NodeName("List.AddItemToEnd")]
    [return: NodeName("list")]
    [NodeDescription("Appends a value to the end of a list (returns a new list).")]
    [NodeSearchTags("append", "push", "add")]
    public static IList<object?> AddItemToEnd(IList<object?> list, object? item)
    {
        RequireList(list, "List.AddItemToEnd");
        var result = new List<object?>(list.Count + 1);
        result.AddRange(list);
        result.Add(item);
        return result;
    }

    /// <summary>Concatenates two lists into one (inputs are not modified).</summary>
    /// <param name="listA">The first list.</param>
    /// <param name="listB">The second list.</param>
    /// <returns>A new list with the elements of both, in order.</returns>
    [NodeName("List.Join")]
    [return: NodeName("list")]
    [NodeDescription("Concatenates two lists into one.")]
    [NodeSearchTags("concat", "combine", "merge", "append")]
    public static IList<object?> Join(IList<object?> listA, IList<object?> listB)
    {
        if (listA == null)
        {
            throw new ArgumentNullException(nameof(listA), "List.Join requires two lists. Wire a list into the 'listA' input.");
        }

        if (listB == null)
        {
            throw new ArgumentNullException(nameof(listB), "List.Join requires two lists. Wire a list into the 'listB' input.");
        }

        var result = new List<object?>(listA.Count + listB.Count);
        result.AddRange(listA);
        result.AddRange(listB);
        return result;
    }

    /// <summary>
    /// Removes the element at an index (negative indexes count from the end).
    /// Returns a new list; the input is not modified.
    /// </summary>
    /// <param name="list">The list to remove from.</param>
    /// <param name="index">Zero-based index; negative values count from the end.</param>
    /// <returns>A new list without the element.</returns>
    [NodeName("List.RemoveItemAtIndex")]
    [return: NodeName("list")]
    [NodeDescription("Removes the element at the given index (negative indexes count from the end).")]
    [NodeSearchTags("delete", "drop", "without")]
    public static IList<object?> RemoveItemAtIndex(IList<object?> list, int index)
    {
        RequireList(list, "List.RemoveItemAtIndex");
        var effective = index < 0 ? list.Count + index : index;
        if (effective < 0 || effective >= list.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Index " + index.ToString(CultureInfo.InvariantCulture) +
                " is out of range for a list of " + list.Count.ToString(CultureInfo.InvariantCulture) + " element(s).");
        }

        var result = new List<object?>(list);
        result.RemoveAt(effective);
        return result;
    }

    /// <summary>
    /// Groups list elements by a parallel list of keys (same length). Groups
    /// appear in order of each key's first occurrence; keys compare with the
    /// same coercing equality as the Equals node.
    /// </summary>
    /// <param name="list">The elements to group.</param>
    /// <param name="keys">One key per element.</param>
    /// <returns>Dictionary with "groups" (list of lists) and "uniqueKeys".</returns>
    [NodeName("List.GroupByKey")]
    [MultiReturn("groups", "uniqueKeys")]
    [NodeDescription("Groups list elements by a parallel key list; returns the groups and their unique keys.")]
    [NodeSearchTags("group", "bucket", "categorize", "partition")]
    public static Dictionary<string, object> GroupByKey(IList<object?> list, IList<object?> keys)
    {
        RequireParallelKeys(list, keys, "List.GroupByKey");

        var uniqueKeys = new List<object?>();
        var groups = new List<object?>();
        var indexByKey = new Dictionary<object?, int>(NodeValueEqualityComparer.Instance);
        var nullKeyIndex = -1;
        for (int i = 0; i < list.Count; i++)
        {
            var key = keys[i];
            int groupIndex;
            if (key == null)
            {
                if (nullKeyIndex < 0)
                {
                    nullKeyIndex = groups.Count;
                    uniqueKeys.Add(null);
                    groups.Add(new List<object?>());
                }

                groupIndex = nullKeyIndex;
            }
            else if (!indexByKey.TryGetValue(key, out groupIndex))
            {
                groupIndex = groups.Count;
                indexByKey[key] = groupIndex;
                uniqueKeys.Add(key);
                groups.Add(new List<object?>());
            }

            ((List<object?>)groups[groupIndex]!).Add(list[i]);
        }

        return new Dictionary<string, object>
        {
            ["groups"] = groups,
            ["uniqueKeys"] = uniqueKeys,
        };
    }

    /// <summary>
    /// Sorts list elements by a parallel list of keys (same length). The sort
    /// is stable and ascending (numbers numerically, strings ordinally); the
    /// input lists are not modified.
    /// </summary>
    /// <param name="list">The elements to sort.</param>
    /// <param name="keys">One sort key per element.</param>
    /// <returns>Dictionary with "sorted" elements and the "sortedKeys".</returns>
    [NodeName("List.SortByKey")]
    [MultiReturn("sorted", "sortedKeys")]
    [NodeDescription("Sorts list elements by a parallel key list; returns the sorted elements and keys.")]
    [NodeSearchTags("order", "arrange", "rank", "key")]
    public static Dictionary<string, object> SortByKey(IList<object?> list, IList<object?> keys)
    {
        RequireParallelKeys(list, keys, "List.SortByKey");

        var order = Enumerable.Range(0, list.Count)
            .OrderBy(i => keys[i], NodeValueComparer.Instance)
            .ToList();

        return new Dictionary<string, object>
        {
            ["sorted"] = order.Select(i => list[i]).ToList(),
            ["sortedKeys"] = order.Select(i => keys[i]).ToList(),
        };
    }

    private static int IndexOfValue(IList<object?> list, object? item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ValueComparison.AreEqual(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    private static void RequireParallelKeys(IList<object?>? list, IList<object?>? keys, string nodeName)
    {
        RequireList(list, nodeName);
        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys), nodeName + " requires a key list. Wire a list into the 'keys' input.");
        }

        if (list!.Count != keys.Count)
        {
            throw new ArgumentException(
                nodeName + " requires the list (" + list.Count.ToString(CultureInfo.InvariantCulture) +
                " element(s)) and the keys (" + keys.Count.ToString(CultureInfo.InvariantCulture) +
                " element(s)) to have the same length.");
        }
    }

    private static void RequireList(IList<object?>? list, string nodeName)
    {
        if (list == null)
        {
            throw new ArgumentNullException(nameof(list), nodeName + " requires a list. Wire a list (e.g. from List.Create) into the 'list' input.");
        }
    }

    private static void FlattenInto(IEnumerable source, int remaining, List<object?> output)
    {
        foreach (var item in source)
        {
            if (remaining != 0 && item is IList nested && !(item is string))
            {
                FlattenInto(nested, remaining - 1, output);
            }
            else
            {
                output.Add(item);
            }
        }
    }

    /// <summary>Ordering comparer delegating to <see cref="ValueComparison.Compare"/>.</summary>
    private sealed class NodeValueComparer : IComparer<object?>
    {
        public static readonly NodeValueComparer Instance = new NodeValueComparer();

        public int Compare(object? x, object? y) => ValueComparison.Compare(x, y);
    }

    /// <summary>Equality comparer delegating to <see cref="ValueComparison.AreEqual"/>.</summary>
    private sealed class NodeValueEqualityComparer : IEqualityComparer<object?>
    {
        public static readonly NodeValueEqualityComparer Instance = new NodeValueEqualityComparer();

        public new bool Equals(object? x, object? y) => ValueComparison.AreEqual(x, y);

        public int GetHashCode(object? obj) => ValueComparison.GetValueHashCode(obj);
    }
}
