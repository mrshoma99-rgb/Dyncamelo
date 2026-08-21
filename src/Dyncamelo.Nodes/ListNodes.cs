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
            // A null mask entry (e.g. a laced check that emitted null for a
            // missing element) counts as false — the element goes to "out"
            // instead of the whole node failing.
            bool flag = false;
            if (mask[i] != null)
            {
                if (!TypeCoercion.TryCoerce(mask[i], typeof(bool), out var coerced) || !(coerced is bool))
                {
                    throw new ArgumentException(
                        "Mask element at index " + i.ToString(CultureInfo.InvariantCulture) + " is not a boolean.");
                }

                flag = (bool)coerced;
            }

            if (flag)
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
        return SortDescribingErrors(list, item => item, "List.Sort");
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

        var order = SortDescribingErrors(
            Enumerable.Range(0, list.Count), i => keys[i], "List.SortByKey");

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

    // ── Dynamo-parity wave (v0.31): indices, editing, sublists, sets, bools ──

    /// <summary>Every index at which an item occurs in a list.</summary>
    /// <param name="list">The list to search.</param>
    /// <param name="item">The value to look for (value equality, like List.IndexOf).</param>
    /// <returns>The zero-based indices of every occurrence (empty when absent).</returns>
    [NodeName("List.AllIndicesOf")]
    [return: NodeName("indices")]
    [NodeDescription("Every zero-based index at which the item occurs in the list — List.IndexOf finds only the first. Feed the indices to List.GetItemAtIndex on a parallel list to pull the matching entries.")]
    [NodeSearchTags("indices", "index", "all", "occurrences", "find", "positions", "where")]
    public static List<int> AllIndicesOf(IList<object?> list, object? item)
    {
        RequireList(list, "List.AllIndicesOf");
        var indices = new List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            if (ValueComparison.AreEqual(list[i], item))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    /// <summary>The index of the LAST occurrence of an item.</summary>
    /// <param name="list">The list to search.</param>
    /// <param name="item">The value to look for (value equality).</param>
    /// <returns>The zero-based index of the last occurrence, or -1 when absent.</returns>
    [NodeName("List.LastIndexOf")]
    [return: NodeName("index")]
    [NodeDescription("The zero-based index of the LAST occurrence of the item (-1 when absent) — the back-to-front twin of List.IndexOf.")]
    [NodeSearchTags("index", "last", "find", "position", "reverse")]
    public static int LastIndexOf(IList<object?> list, object? item)
    {
        RequireList(list, "List.LastIndexOf");
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (ValueComparison.AreEqual(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Replaces null elements with a substitute value, descending into nested
    /// lists so every level is patched.
    /// </summary>
    /// <param name="list">The list to patch (nested lists are patched recursively).</param>
    /// <param name="substitute">The value to put where a null sits (e.g. 0, "" or "n/a").</param>
    /// <returns>The patched list, same shape as the input.</returns>
    [NodeName("List.ReplaceNulls")]
    [return: NodeName("list")]
    [NodeDescription("Replaces every null element with a substitute value, at every nesting level — keeps list lengths and alignment intact where List.Clean would shift them. The gap-filler for laced calls that emitted nulls (0 for math, \"n/a\" for reports).")]
    [NodeSearchTags("replace", "null", "nulls", "substitute", "default", "fill", "patch")]
    public static IList<object?> ReplaceNulls(IList<object?> list, object? substitute)
    {
        RequireList(list, "List.ReplaceNulls");
        return ReplaceNullsInto(list, substitute);
    }

    /// <summary>Replaces the element at an index.</summary>
    /// <param name="list">The list to edit.</param>
    /// <param name="index">Zero-based index; negative values count from the end.</param>
    /// <param name="item">The replacement value.</param>
    /// <returns>A new list with the element replaced.</returns>
    [NodeName("List.ReplaceItemAtIndex")]
    [return: NodeName("list")]
    [NodeDescription("Returns a new list with the element at the index replaced (negative indexes count from the end).")]
    [NodeSearchTags("replace", "item", "index", "set", "edit")]
    public static IList<object?> ReplaceItemAtIndex(IList<object?> list, int index, object? item)
    {
        RequireList(list, "List.ReplaceItemAtIndex");
        var effective = NormalizeIndex(index, list.Count, "List.ReplaceItemAtIndex");
        var output = new List<object?>(list);
        output[effective] = item;
        return output;
    }

    /// <summary>Inserts an element at an index.</summary>
    /// <param name="list">The list to edit.</param>
    /// <param name="item">The value to insert.</param>
    /// <param name="index">Zero-based position for the new element (0 = front, Count = end; negative counts from the end).</param>
    /// <returns>A new list with the element inserted.</returns>
    [NodeName("List.Insert")]
    [return: NodeName("list")]
    [NodeDescription("Returns a new list with the value inserted at the index (0 = front; the list's length = append; negative counts from the end).")]
    [NodeSearchTags("insert", "add", "index", "position")]
    public static IList<object?> Insert(IList<object?> list, object? item, int index)
    {
        RequireList(list, "List.Insert");
        var effective = index < 0 ? list.Count + index : index;
        if (effective < 0 || effective > list.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Index " + index.ToString(CultureInfo.InvariantCulture) +
                " is out of range for inserting into a list of " + list.Count.ToString(CultureInfo.InvariantCulture) + " element(s).");
        }

        var output = new List<object?>(list);
        output.Insert(effective, item);
        return output;
    }

    /// <summary>Adds an element at the front of a list.</summary>
    /// <param name="list">The list to extend.</param>
    /// <param name="item">The value to prepend.</param>
    /// <returns>A new list with the element first.</returns>
    [NodeName("List.AddItemToFront")]
    [return: NodeName("list")]
    [NodeDescription("Returns a new list with the value prepended — the front-side twin of List.AddItemToEnd.")]
    [NodeSearchTags("add", "prepend", "front", "first", "push")]
    public static IList<object?> AddItemToFront(IList<object?> list, object? item)
    {
        RequireList(list, "List.AddItemToFront");
        var output = new List<object?>(list.Count + 1) { item };
        output.AddRange(list);
        return output;
    }

    /// <summary>Everything but the first element.</summary>
    /// <param name="list">The list to read from (must not be empty).</param>
    /// <returns>The list without its first element.</returns>
    [NodeName("List.RestOfItems")]
    [return: NodeName("list")]
    [NodeDescription("Everything but the first element — pairs with List.FirstItem for head/tail processing.")]
    [NodeSearchTags("rest", "tail", "skip", "first")]
    public static IList<object?> RestOfItems(IList<object?> list)
    {
        RequireList(list, "List.RestOfItems");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.RestOfItems requires a non-empty list.");
        }

        var output = new List<object?>(list);
        output.RemoveAt(0);
        return output;
    }

    /// <summary>Removes elements from the start (or, negative, the end) of a list.</summary>
    /// <param name="list">The list to shorten.</param>
    /// <param name="amount">How many elements to drop: positive from the start, negative from the end.</param>
    /// <returns>The shortened list (empty when the amount exceeds the length).</returns>
    [NodeName("List.DropItems")]
    [return: NodeName("list")]
    [NodeDescription("Drops elements from the start of the list — or from the END with a negative amount (Dynamo behavior). Dropping more than the length gives an empty list.")]
    [NodeSearchTags("drop", "skip", "remove", "trim", "start", "end")]
    public static IList<object?> DropItems(IList<object?> list, int amount)
    {
        RequireList(list, "List.DropItems");
        var count = Math.Min(Math.Abs(amount), list.Count);
        var output = new List<object?>(list);
        if (amount >= 0)
        {
            output.RemoveRange(0, count);
        }
        else
        {
            output.RemoveRange(list.Count - count, count);
        }

        return output;
    }

    /// <summary>Takes elements from the start (or, negative, the end) of a list.</summary>
    /// <param name="list">The list to read from.</param>
    /// <param name="amount">How many elements to keep: positive from the start, negative from the end.</param>
    /// <returns>The taken elements (the whole list when the amount exceeds the length).</returns>
    [NodeName("List.TakeItems")]
    [return: NodeName("list")]
    [NodeDescription("Takes elements from the start of the list — or from the END with a negative amount (Dynamo behavior). Taking more than the length gives the whole list.")]
    [NodeSearchTags("take", "first", "head", "keep", "start", "end")]
    public static IList<object?> TakeItems(IList<object?> list, int amount)
    {
        RequireList(list, "List.TakeItems");
        var count = Math.Min(Math.Abs(amount), list.Count);
        var from = amount >= 0 ? 0 : list.Count - count;
        var output = new List<object?>(count);
        for (int i = 0; i < count; i++)
        {
            output.Add(list[from + i]);
        }

        return output;
    }

    /// <summary>A sub-range of a list.</summary>
    /// <param name="list">The list to read from.</param>
    /// <param name="start">First index of the range (inclusive; negative counts from the end).</param>
    /// <param name="end">End of the range (exclusive; negative counts from the end).</param>
    /// <param name="step">Take every step-th element of the range (≥ 1).</param>
    /// <returns>The sub-list.</returns>
    [NodeName("List.Slice")]
    [return: NodeName("list")]
    [NodeDescription("A sub-range of the list: from start (inclusive) to end (exclusive), taking every step-th element. Negative start/end count from the end, Python-style.")]
    [NodeSearchTags("slice", "range", "sub", "subset", "portion", "between")]
    public static IList<object?> Slice(IList<object?> list, int start, int end, int step = 1)
    {
        RequireList(list, "List.Slice");
        if (step < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "List.Slice requires a step of at least 1.");
        }

        var from = start < 0 ? Math.Max(0, list.Count + start) : Math.Min(start, list.Count);
        var to = end < 0 ? Math.Max(0, list.Count + end) : Math.Min(end, list.Count);
        var output = new List<object?>();
        for (int i = from; i < to; i += step)
        {
            output.Add(list[i]);
        }

        return output;
    }

    /// <summary>Chops a list into consecutive sublists of the given lengths.</summary>
    /// <param name="list">The list to chop.</param>
    /// <param name="lengths">Sublist length(s): one number chops evenly; a list of numbers is applied in sequence and repeats until the list is used up.</param>
    /// <returns>The sublists (the last one may be shorter).</returns>
    [NodeName("List.Chop")]
    [return: NodeName("lists")]
    [NodeDescription("Chops a list into consecutive sublists: one length chops evenly ([1..7] by 3 → [1,2,3],[4,5,6],[7]); a list of lengths is applied in sequence and repeats until the input runs out (Dynamo behavior).")]
    [NodeSearchTags("chop", "split", "partition", "chunk", "sublists", "group")]
    public static IList<object?> Chop(IList<object?> list, IList<object?> lengths)
    {
        RequireList(list, "List.Chop");
        if (lengths == null || lengths.Count == 0)
        {
            throw new ArgumentException("List.Chop requires at least one sublist length.", nameof(lengths));
        }

        var sizes = new List<int>(lengths.Count);
        foreach (var length in lengths)
        {
            var size = Convert.ToInt32(length, CultureInfo.InvariantCulture);
            if (size < 1)
            {
                throw new ArgumentException("Sublist lengths must be at least 1 (got " + size.ToString(CultureInfo.InvariantCulture) + ").", nameof(lengths));
            }

            sizes.Add(size);
        }

        var output = new List<object?>();
        int position = 0, sizeIndex = 0;
        while (position < list.Count)
        {
            var take = Math.Min(sizes[sizeIndex % sizes.Count], list.Count - position);
            var chunk = new List<object?>(take);
            for (int i = 0; i < take; i++)
            {
                chunk.Add(list[position + i]);
            }

            output.Add(chunk);
            position += take;
            sizeIndex++;
        }

        return output;
    }

    /// <summary>Swaps the rows and columns of a list of lists.</summary>
    /// <param name="list">The list of rows.</param>
    /// <returns>The transposed list; shorter rows are padded with nulls so the result is rectangular (Dynamo behavior).</returns>
    [NodeName("List.Transpose")]
    [return: NodeName("lists")]
    [NodeDescription("Swaps rows and columns of a list of lists — the table pivot for Excel/CSV data and property grids. Shorter rows pad with nulls so the result stays rectangular (Dynamo behavior); List.Clean or List.ReplaceNulls deal with the padding.")]
    [NodeSearchTags("transpose", "rows", "columns", "pivot", "swap", "table", "matrix")]
    public static IList<object?> Transpose(IList<object?> list)
    {
        RequireList(list, "List.Transpose");
        var rows = new List<IList<object?>>();
        int width = 0;
        foreach (var element in list)
        {
            var row = element is IList raw && !(element is string)
                ? Materialize(raw)
                : new List<object?> { element };
            rows.Add(row);
            width = Math.Max(width, row.Count);
        }

        var output = new List<object?>(width);
        for (int column = 0; column < width; column++)
        {
            var transposed = new List<object?>(rows.Count);
            foreach (var row in rows)
            {
                transposed.Add(column < row.Count ? row[column] : null);
            }

            output.Add(transposed);
        }

        return output;
    }

    /// <summary>Repeats a whole list a number of times.</summary>
    /// <param name="list">The list to repeat.</param>
    /// <param name="amount">How many copies to chain (≥ 0).</param>
    /// <returns>The list repeated end-to-end.</returns>
    [NodeName("List.Cycle")]
    [return: NodeName("list")]
    [NodeDescription("Repeats the whole list a number of times, end-to-end ([a,b] × 3 → [a,b,a,b,a,b]).")]
    [NodeSearchTags("cycle", "repeat", "tile", "loop", "duplicate")]
    public static IList<object?> Cycle(IList<object?> list, int amount)
    {
        RequireList(list, "List.Cycle");
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "List.Cycle requires a non-negative amount.");
        }

        var output = new List<object?>(list.Count * amount);
        for (int i = 0; i < amount; i++)
        {
            output.AddRange(list);
        }

        return output;
    }

    /// <summary>A list made of one value repeated.</summary>
    /// <param name="item">The value to repeat.</param>
    /// <param name="amount">How many copies (≥ 0).</param>
    /// <returns>The repeated-value list.</returns>
    [NodeName("List.OfRepeatedItem")]
    [return: NodeName("list")]
    [NodeDescription("A list of one value repeated N times — constant columns for tables, or a fixed pairing partner under Longest lacing.")]
    [NodeSearchTags("repeat", "repeated", "fill", "constant", "duplicate")]
    public static IList<object?> OfRepeatedItem(object? item, int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "List.OfRepeatedItem requires a non-negative amount.");
        }

        var output = new List<object?>(amount);
        for (int i = 0; i < amount; i++)
        {
            output.Add(item);
        }

        return output;
    }

    /// <summary>The largest element of a list.</summary>
    /// <param name="list">The list to scan (must not be empty; nulls are ignored).</param>
    /// <returns>The maximum element.</returns>
    [NodeName("List.MaximumItem")]
    [return: NodeName("item")]
    [NodeDescription("The largest element of a list (numbers, texts or dates; nulls are ignored).")]
    [NodeSearchTags("maximum", "max", "largest", "biggest", "highest")]
    public static object? MaximumItem(IList<object?> list)
    {
        return Extreme(list, "List.MaximumItem", larger: true);
    }

    /// <summary>The smallest element of a list.</summary>
    /// <param name="list">The list to scan (must not be empty; nulls are ignored).</param>
    /// <returns>The minimum element.</returns>
    [NodeName("List.MinimumItem")]
    [return: NodeName("item")]
    [NodeDescription("The smallest element of a list (numbers, texts or dates; nulls are ignored).")]
    [NodeSearchTags("minimum", "min", "smallest", "lowest")]
    public static object? MinimumItem(IList<object?> list)
    {
        return Extreme(list, "List.MinimumItem", larger: false);
    }

    /// <summary>The distinct elements present in either list.</summary>
    /// <param name="list1">The first list.</param>
    /// <param name="list2">The second list.</param>
    /// <returns>The union, first-seen order, duplicates removed.</returns>
    [NodeName("List.SetUnion")]
    [return: NodeName("list")]
    [NodeDescription("The distinct elements present in EITHER list (value equality, first-seen order) — combine two item sets without duplicates.")]
    [NodeSearchTags("union", "set", "combine", "merge", "distinct", "or")]
    public static IList<object?> SetUnion(IList<object?> list1, IList<object?> list2)
    {
        RequireList(list1, "List.SetUnion");
        RequireList(list2, "List.SetUnion");
        var seen = new HashSet<object?>(NodeValueEqualityComparer.Instance);
        var output = new List<object?>();
        foreach (var element in Concat(list1, list2))
        {
            if (seen.Add(element))
            {
                output.Add(element);
            }
        }

        return output;
    }

    /// <summary>The distinct elements present in both lists.</summary>
    /// <param name="list1">The first list.</param>
    /// <param name="list2">The second list.</param>
    /// <returns>The intersection, ordered as in the first list.</returns>
    [NodeName("List.SetIntersection")]
    [return: NodeName("list")]
    [NodeDescription("The distinct elements present in BOTH lists (value equality, ordered as in the first) — what two searches/sets have in common.")]
    [NodeSearchTags("intersection", "set", "common", "both", "and", "overlap")]
    public static IList<object?> SetIntersection(IList<object?> list1, IList<object?> list2)
    {
        RequireList(list1, "List.SetIntersection");
        RequireList(list2, "List.SetIntersection");
        var inSecond = new HashSet<object?>(list2, NodeValueEqualityComparer.Instance);
        var seen = new HashSet<object?>(NodeValueEqualityComparer.Instance);
        var output = new List<object?>();
        foreach (var element in list1)
        {
            if (inSecond.Contains(element) && seen.Add(element))
            {
                output.Add(element);
            }
        }

        return output;
    }

    /// <summary>The distinct elements of the first list that are not in the second.</summary>
    /// <param name="list1">The list to start from.</param>
    /// <param name="list2">The elements to remove.</param>
    /// <returns>The difference, ordered as in the first list.</returns>
    [NodeName("List.SetDifference")]
    [return: NodeName("list")]
    [NodeDescription("The distinct elements of the FIRST list that are NOT in the second (value equality) — subtract an ignore-list from a result set.")]
    [NodeSearchTags("difference", "set", "subtract", "except", "remove", "without")]
    public static IList<object?> SetDifference(IList<object?> list1, IList<object?> list2)
    {
        RequireList(list1, "List.SetDifference");
        RequireList(list2, "List.SetDifference");
        var inSecond = new HashSet<object?>(list2, NodeValueEqualityComparer.Instance);
        var seen = new HashSet<object?>(NodeValueEqualityComparer.Instance);
        var output = new List<object?>();
        foreach (var element in list1)
        {
            if (!inSecond.Contains(element) && seen.Add(element))
            {
                output.Add(element);
            }
        }

        return output;
    }

    /// <summary>Every n-th element of a list.</summary>
    /// <param name="list">The list to sample.</param>
    /// <param name="n">Take every n-th element (≥ 1).</param>
    /// <param name="offset">Elements to skip before the first take.</param>
    /// <returns>The sampled elements.</returns>
    [NodeName("List.TakeEveryNthItem")]
    [return: NodeName("list")]
    [NodeDescription("Every n-th element, optionally after skipping offset elements — thin out a dense list (n=2 halves it).")]
    [NodeSearchTags("every", "nth", "sample", "skip", "thin", "step")]
    public static IList<object?> TakeEveryNthItem(IList<object?> list, int n, int offset = 0)
    {
        RequireList(list, "List.TakeEveryNthItem");
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "List.TakeEveryNthItem requires n of at least 1.");
        }

        var output = new List<object?>();
        for (int i = Math.Max(0, offset) + n - 1; i < list.Count; i += n)
        {
            output.Add(list[i]);
        }

        return output;
    }

    /// <summary>Rotates a list's elements by an amount.</summary>
    /// <param name="list">The list to rotate.</param>
    /// <param name="amount">Positive moves elements towards the end (the last wraps to the front); negative the other way.</param>
    /// <returns>The rotated list.</returns>
    [NodeName("List.ShiftIndices")]
    [return: NodeName("list")]
    [NodeDescription("Rotates the list: +1 moves every element one place towards the end and wraps the last to the front ([a,b,c] → [c,a,b]); negative rotates the other way.")]
    [NodeSearchTags("shift", "rotate", "wrap", "offset", "roll")]
    public static IList<object?> ShiftIndices(IList<object?> list, int amount)
    {
        RequireList(list, "List.ShiftIndices");
        var output = new List<object?>(list.Count);
        if (list.Count == 0)
        {
            return output;
        }

        var shift = ((amount % list.Count) + list.Count) % list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            output.Add(list[(i - shift + list.Count) % list.Count]);
        }

        return output;
    }

    /// <summary>Whether every element of a boolean list is true.</summary>
    /// <param name="list">The booleans to test (nulls and non-booleans count as not-true).</param>
    /// <returns>True when every element is true (and the list is not empty).</returns>
    [NodeName("List.AllTrue")]
    [return: NodeName("allTrue")]
    [NodeDescription("True when EVERY element of the list is true — collapse a mask into one verdict (an empty list gives false; nulls count as not-true).")]
    [NodeSearchTags("all", "true", "every", "and", "mask", "verdict")]
    public static bool AllTrue(IList<object?> list)
    {
        RequireList(list, "List.AllTrue");
        if (list.Count == 0)
        {
            return false;
        }

        foreach (var element in list)
        {
            if (!IsTrue(element))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether any element of a boolean list is true.</summary>
    /// <param name="list">The booleans to test (nulls and non-booleans count as not-true).</param>
    /// <returns>True when at least one element is true.</returns>
    [NodeName("List.AnyTrue")]
    [return: NodeName("anyTrue")]
    [NodeDescription("True when AT LEAST ONE element of the list is true — \"did anything match?\" in one node (nulls count as not-true).")]
    [NodeSearchTags("any", "true", "some", "or", "mask", "exists")]
    public static bool AnyTrue(IList<object?> list)
    {
        RequireList(list, "List.AnyTrue");
        foreach (var element in list)
        {
            if (IsTrue(element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many elements of a boolean list are true (and how many are not).</summary>
    /// <param name="list">The booleans to count (nulls and non-booleans count as not-true).</param>
    /// <returns>The true and not-true counts.</returns>
    [NodeName("List.CountTrue")]
    [MultiReturn("trueCount", "falseCount")]
    [NodeDescription("Counts the true and not-true elements of a mask — \"37 of 340 matched\" for reports without filtering first (nulls count as not-true).")]
    [NodeSearchTags("count", "true", "false", "mask", "tally", "how many")]
    public static Dictionary<string, object> CountTrue(IList<object?> list)
    {
        RequireList(list, "List.CountTrue");
        int trueCount = 0;
        foreach (var element in list)
        {
            if (IsTrue(element))
            {
                trueCount++;
            }
        }

        return new Dictionary<string, object>
        {
            ["trueCount"] = trueCount,
            ["falseCount"] = list.Count - trueCount,
        };
    }

    /// <summary>
    /// Removes null elements from a list, descending into nested lists so a
    /// list-of-lists comes back with every level cleaned.
    /// </summary>
    /// <param name="list">The list to clean (nested lists are cleaned recursively).</param>
    /// <param name="removeEmptyLists">True also drops sublists that are (or become) empty; false keeps them as empty lists.</param>
    /// <returns>The cleaned list.</returns>
    [NodeName("List.Clean")]
    [return: NodeName("list")]
    [NodeDescription(
        "Removes null elements from a list, at every nesting level — the mop-up after laced calls that " +
        "emitted nulls for missing elements (a yellow node badge points here). removeEmptyLists also " +
        "drops sublists left empty. Dynamo users: same idea as List.Clean.")]
    [NodeSearchTags("clean", "null", "remove", "nulls", "compact", "purge", "empty", "filter")]
    public static IList<object?> Clean(IList<object?> list, bool removeEmptyLists = true)
    {
        RequireList(list, "List.Clean");
        return CleanInto(list, removeEmptyLists);
    }

    private static List<object?> ReplaceNullsInto(IEnumerable source, object? substitute)
    {
        var output = new List<object?>();
        foreach (var element in source)
        {
            if (element == null)
            {
                output.Add(substitute);
            }
            else if (element is IList nested && !(element is string))
            {
                output.Add(ReplaceNullsInto(nested, substitute));
            }
            else
            {
                output.Add(element);
            }
        }

        return output;
    }

    /// <summary>Resolves a possibly-negative index against a count, throwing the standard range error.</summary>
    private static int NormalizeIndex(int index, int count, string nodeName)
    {
        var effective = index < 0 ? count + index : index;
        if (effective < 0 || effective >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                "Index " + index.ToString(CultureInfo.InvariantCulture) +
                " is out of range for a list of " + count.ToString(CultureInfo.InvariantCulture) + " element(s) (" + nodeName + ").");
        }

        return effective;
    }

    private static List<object?> Materialize(IEnumerable source)
    {
        var output = new List<object?>();
        foreach (var element in source)
        {
            output.Add(element);
        }

        return output;
    }

    private static IEnumerable<object?> Concat(IList<object?> first, IList<object?> second)
    {
        foreach (var element in first)
        {
            yield return element;
        }

        foreach (var element in second)
        {
            yield return element;
        }
    }

    /// <summary>The largest/smallest non-null element by node value ordering.</summary>
    private static object? Extreme(IList<object?> list, string nodeName, bool larger)
    {
        RequireList(list, nodeName);
        object? best = null;
        bool found = false;
        foreach (var element in list)
        {
            if (element == null)
            {
                continue;
            }

            if (!found)
            {
                best = element;
                found = true;
                continue;
            }

            var comparison = ValueComparison.Compare(element, best);
            if (larger ? comparison > 0 : comparison < 0)
            {
                best = element;
            }
        }

        if (!found)
        {
            throw new InvalidOperationException(nodeName + " requires at least one non-null element.");
        }

        return best;
    }

    /// <summary>True only for a value that reads as boolean true (nulls and non-booleans are not-true).</summary>
    private static bool IsTrue(object? element)
    {
        return element != null &&
               TypeCoercion.TryCoerce(element, typeof(bool), out var coerced) &&
               coerced is bool flag && flag;
    }

    private static List<object?> CleanInto(IEnumerable source, bool removeEmptyLists)
    {
        var output = new List<object?>();
        foreach (var element in source)
        {
            if (element == null)
            {
                continue;
            }

            if (element is IList nested && !(element is string))
            {
                var cleaned = CleanInto(nested, removeEmptyLists);
                if (removeEmptyLists && cleaned.Count == 0)
                {
                    continue;
                }

                output.Add(cleaned);
                continue;
            }

            output.Add(element);
        }

        return output;
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

    /// <summary>
    /// Sorts with <see cref="NodeValueComparer"/>, unwrapping LINQ's generic
    /// "Failed to compare two elements in the array." wrapper so the node error
    /// carries <see cref="ValueComparison.Compare"/>'s descriptive message.
    /// </summary>
    private static List<T> SortDescribingErrors<T>(IEnumerable<T> source, Func<T, object?> keySelector, string nodeName)
    {
        try
        {
            return source.OrderBy(keySelector, NodeValueComparer.Instance).ToList();
        }
        catch (InvalidOperationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(nodeName + ": " + ex.InnerException.Message, ex.InnerException);
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
