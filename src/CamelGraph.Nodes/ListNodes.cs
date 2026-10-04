using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;
using CamelGraph.Nodes.Internal;

namespace CamelGraph.Nodes;

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
    [NodeDescription("Returns the first element of a list. An empty list is an error: there is no first element (check List.Count first, or use List.Slice, which gives an empty list).")]
    [NodeSearchTags("head", "front")]
    public static object? FirstItem(IList<object?> list)
    {
        RequireList(list, "List.FirstItem");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.FirstItem: the list is empty, so it has no first element.");
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
    public static IList<object?> Flatten(IList<object?> list, [NodeRange(-1, 100)] int amount = -1)
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
    [PortKinds("", "")]
    [NodeDescription("Splits a list into elements whose mask entry is true (\"in\") and the rest (\"out\"). The mask must have one entry per element: true or false, the text \"true\" or \"false\", or a number (0 is false, any other number true); an empty entry (null) counts as false, so the element goes to \"out\". A list or dictionary in the mask is an error: set both inputs to @L2 (right-click, List Levels) to filter each pair of sublists, or flatten them first.")]
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
            if (ReadSplitMask(mask[i], i, "List.FilterByBoolMask"))
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

    /// <summary>The most items a node that builds a list from a number may make (one million). A wrong number must not exhaust the memory of the host.</summary>
    internal const int MaxListSize = 1000000;

    /// <summary>
    /// Produces a numeric sequence from start towards end (inclusive, with a
    /// small tolerance for floating-point drift). A step moving away from end
    /// yields an empty list; a zero step is an error, and so is a sequence of
    /// more than one million numbers. Each value is start + n * step, so the
    /// error does not add up along the list.
    /// </summary>
    /// <param name="start">First value of the sequence.</param>
    /// <param name="end">Inclusive upper (or lower, for negative steps) bound.</param>
    /// <param name="step">Increment between values; may be negative.</param>
    /// <returns>The sequence as a list of numbers.</returns>
    [NodeName("List.Range")]
    [return: NodeName("list")]
    [NodeDescription("Creates a sequence of numbers from start to end (both included) using the given step; a negative step counts down, a step that moves away from end gives an empty list. Each number is start + n x step, so 0.1 steps do not drift. At most 1,000,000 numbers: a larger sequence is an error, so a slip of a zero cannot use up the memory of Navisworks.")]
    [NodeSearchTags("sequence", "series", "numbers")]
    public static IList<double> Range(double start, double end, double step = 1d)
    {
        if (double.IsNaN(start) || double.IsInfinity(start) || double.IsNaN(end) || double.IsInfinity(end) || double.IsNaN(step) || double.IsInfinity(step))
        {
            throw new ArgumentException(
                "List.Range needs finite numbers for start, end and step (got start " + start.ToString(CultureInfo.InvariantCulture) +
                ", end " + end.ToString(CultureInfo.InvariantCulture) + ", step " + step.ToString(CultureInfo.InvariantCulture) + ").");
        }

        if (step == 0d)
        {
            throw new ArgumentException("List.Range requires a non-zero step.", nameof(step));
        }

        var result = new List<double>();
        var span = end - start;
        if (step > 0d ? span < 0d : span > 0d)
        {
            return result;
        }

        // The last number n with start + n x step still at (or, by the old 1e-9 tolerance, a hair beyond) end.
        var last = Math.Floor((span / step) + 1e-9);
        if (double.IsNaN(last) || double.IsInfinity(last) || last + 1d > MaxListSize)
        {
            throw new ArgumentException(
                "List.Range would make " + FormatCount(last + 1d) + " numbers; the limit is " + MaxListSize.ToString("N0", CultureInfo.InvariantCulture) +
                ". Use a larger step or a shorter range (the limit protects the memory of Navisworks).");
        }

        var count = (int)last + 1;
        result.Capacity = count;
        for (var n = 0; n < count; n++)
        {
            result.Add(start + (n * step));
        }

        return result;
    }

    private static string FormatCount(double count) =>
        double.IsNaN(count) || double.IsInfinity(count) || count > 1e15 ? "far too many" : count.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Sorts a list, smallest first (or largest first with <paramref name="descending"/>). The order is the one rule the
    /// library uses everywhere (List.Sort, List.SortByKey, List.MaximumItem, List.MinimumItem and Table.Sort): numbers by value,
    /// text alphabetically ignoring upper and lower case, text that reads as a number as a number when the other item is a
    /// number, and empty items (null or empty text) last in either direction. The sort is stable and the input list is not modified.
    /// </summary>
    /// <param name="list">The list to sort.</param>
    /// <param name="descending">True sorts largest first; empty items stay last.</param>
    /// <returns>A new sorted list.</returns>
    [NodeName("List.Sort")]
    [NodeAliases("CamelGraph.Nodes.ListNodes.Sort@System.Collections.Generic.IList<object>")]
    [return: NodeName("list")]
    [NodeDescription("Returns the list sorted ascending, or largest first with descending. Numbers sort by value; text alphabetically, ignoring upper and lower case (\"apple\" comes before \"Zebra\", and \"10\" before \"9\" because both are text); text that reads as a number counts as a number against a number; empty items (null or empty text) always go last, in either direction. Equal items keep their order. A list inside the list cannot be put in order: set the input to @L2 to sort each sublist.")]
    [NodeSearchTags("order", "ascending", "arrange", "descending", "reverse", "biggest first", "top", "sortdescending")]
    public static IList<object?> Sort(IList<object?> list, bool descending = false) => SortItems(list, descending, "List.Sort");

    /// <summary>Sorts a list by the library's ordering rule; the node name only shows in the error text.</summary>
    internal static IList<object?> SortItems(IList<object?> list, bool descending, string nodeName)
    {
        RequireList(list, nodeName);
        var order = OrderOf(list, descending, nodeName);
        var result = new List<object?>(list.Count);
        foreach (var index in order)
        {
            result.Add(list[index]);
        }

        return result;
    }

    /// <summary>
    /// Removes duplicate elements, keeping the first occurrence of each value.
    /// Numbers compare by value regardless of numeric type.
    /// </summary>
    /// <param name="list">The list to deduplicate.</param>
    /// <returns>A new list with duplicates removed, in original order.</returns>
    [NodeName("List.UniqueItems")]
    [return: NodeName("list")]
    [NodeDescription("Removes duplicate elements from a list, keeping the first of each and the original order. Numbers compare by value, text with its case, and lists and dictionaries by their content, so repeated pairs from List.Zip are removed too.")]
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
    [NodeDescription("Returns the last element of a list. An empty list is an error: there is no last element (check List.Count first, or use List.Slice, which gives an empty list).")]
    [NodeSearchTags("tail", "end", "final")]
    public static object? LastItem(IList<object?> list)
    {
        RequireList(list, "List.LastItem");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.LastItem: the list is empty, so it has no last element.");
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
    [NodeDescription("Tests whether a list contains a value. Numbers compare by value regardless of numeric type, text with its case, and a list or dictionary by its content, so a [level, type] pair can be looked up too.")]
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
    [NodeDescription("Returns the index of the first occurrence of a value in a list (-1 when absent). Numbers compare by value, text with its case, and lists and dictionaries by their content.")]
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
    [PortAlias("reversed", "list")]
    [return: NodeName("list")]
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
    [NodeDeprecated("List.Merge")]
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
    /// Concatenates any number of lists into one. The input is a multi-input socket: wire as many lists (or single
    /// values) as you like, in the order you want them.
    /// </summary>
    /// <param name="lists">Every list wired to the socket; list-valued wires contribute their elements, other values themselves.</param>
    /// <returns>A new list with the elements of all the wires, in wire order.</returns>
    [NodeName("List.Merge")]
    [return: NodeName("list")]
    [NodeDescription("Concatenates any number of lists into one — a multi-input socket: connect as many wires as you like, in the order you want them.")]
    [NodeSearchTags("concat", "combine", "join", "append", "many", "multi")]
    public static IList<object?> Merge([MultiInput] IList<object?> lists)
    {
        if (lists == null)
        {
            throw new ArgumentNullException(nameof(lists), "List.Merge requires at least one list. Wire a list into the 'lists' input.");
        }

        return new List<object?>(lists);
    }

    /// <summary>
    /// Removes the elements at one or several indices (negative indexes count from the end).
    /// Returns a new list; the input is not modified.
    /// </summary>
    /// <param name="list">The list to remove from.</param>
    /// <param name="indices">Zero-based indices of the elements to remove; negative values count from the end. A single number works too. The same index twice removes the element once.</param>
    /// <returns>A new list without the elements.</returns>
    [NodeName("List.RemoveItemAtIndex")]
    [NodeAliases("CamelGraph.Nodes.ListNodes.RemoveItemAtIndex@System.Collections.Generic.IList<object>,int")]
    [PortAlias("index", "indices")]
    [return: NodeName("list")]
    [NodeDescription("Removes the elements at the given indices and returns one new list (negative indexes count from the end). Wire a single number to remove one element, or a list of numbers, for example from List.AllIndicesOf, to remove them all in one go. Indices count in the original list; an index outside the list is an error.")]
    [NodeSearchTags("delete", "drop", "without")]
    public static IList<object?> RemoveItemAtIndex(IList<object?> list, IList<int> indices)
    {
        RequireList(list, "List.RemoveItemAtIndex");
        if (indices == null)
        {
            throw new ArgumentNullException(nameof(indices), "List.RemoveItemAtIndex requires the index (or a list of indices) to remove. Wire a number or a list of numbers into the 'indices' input.");
        }

        var remove = new HashSet<int>();
        foreach (var index in indices)
        {
            remove.Add(NormalizeIndex(index, list.Count, "List.RemoveItemAtIndex"));
        }

        var result = new List<object?>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (!remove.Contains(i))
            {
                result.Add(list[i]);
            }
        }

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
    [PortKinds("", "")]
    [NodeDescription("Groups list elements by a parallel key list of the same length; returns the groups (in the order each key first appears) and their unique keys. A key can be a list, such as [level, type], and two keys with the same content make one group; text keys keep their case.")]
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
    /// Sorts list elements by a parallel list of keys (same length). The sort is stable; the keys are put in order by the same
    /// rule as List.Sort (numbers by value, text ignoring case, empty keys last); the input lists are not modified.
    /// </summary>
    /// <param name="list">The elements to sort.</param>
    /// <param name="keys">One sort key per element.</param>
    /// <param name="descending">True sorts the largest key first; elements with an empty key stay last.</param>
    /// <returns>Dictionary with "sorted" elements and the "sortedKeys".</returns>
    [NodeName("List.SortByKey")]
    [NodeAliases("CamelGraph.Nodes.ListNodes.SortByKey@System.Collections.Generic.IList<object>,System.Collections.Generic.IList<object>")]
    [MultiReturn("sorted", "sortedKeys")]
    [PortKinds("", "")]
    [NodeDescription("Sorts list elements by a parallel key list of the same length, smallest key first or largest first with descending; returns the sorted elements and keys. Keys are put in order as in List.Sort (numbers by value, text ignoring case, empty keys last); elements with equal keys keep their order.")]
    [NodeSearchTags("order", "arrange", "rank", "key", "descending")]
    public static Dictionary<string, object> SortByKey(IList<object?> list, IList<object?> keys, bool descending = false)
    {
        RequireParallelKeys(list, keys, "List.SortByKey");

        var order = OrderOf(keys, descending, "List.SortByKey");

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
    [NodeDescription("Every zero-based index at which the item occurs in the list — List.IndexOf finds only the first. Values compare as in List.IndexOf. Feed the indices to List.GetItemAtIndex on a parallel list to pull the matching entries.")]
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
    [NodeDescription("The zero-based index of the LAST occurrence of the item (-1 when absent) — the back-to-front twin of List.IndexOf, with the same rules for comparing values.")]
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
    [NodeDescription("Returns a new list with the element at the index replaced (negative indexes count from the end). A list of indices makes one new list per index (the first index with the first item, and so on); the list itself is always taken whole.")]
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
    [NodeDescription("Returns a new list with the value inserted at the index (0 = front; the list's length = append; negative counts from the end). A list of indices makes one new list per index; the list itself is always taken whole.")]
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
    [NodeDescription("Everything but the first element — pairs with List.FirstItem for head/tail processing. An empty list is an error: there is no first element to leave out.")]
    [NodeSearchTags("rest", "tail", "skip", "first")]
    public static IList<object?> RestOfItems(IList<object?> list)
    {
        RequireList(list, "List.RestOfItems");
        if (list.Count == 0)
        {
            throw new InvalidOperationException("List.RestOfItems: the list is empty, so there is no first element to leave out.");
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
        var count = (int)Math.Min(Math.Abs((long)amount), list.Count);
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
        var count = (int)Math.Min(Math.Abs((long)amount), list.Count);
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
    /// <param name="end">End of the range (exclusive; negative counts from the end); leave it unset to go to the end of the list.</param>
    /// <param name="step">Take every step-th element of the range (≥ 1).</param>
    /// <returns>The sub-list.</returns>
    [NodeName("List.Slice")]
    [NodeAliases("CamelGraph.Nodes.ListNodes.Slice@System.Collections.Generic.IList<object>,int,int,int")]
    [return: NodeName("list")]
    [NodeDescription("A sub-range of the list: from start (inclusive) to end (exclusive), taking every step-th element; leave end empty to go to the end of the list. Negative start/end count from the end, Python-style. Start 0 with a step of 3 keeps the 1st, 4th, 7th ... item, which is how to thin a list out.")]
    [NodeSearchTags("slice", "range", "sub", "subset", "portion", "between", "thin", "to the end")]
    public static IList<object?> Slice(IList<object?> list, int start, int? end = null, [NodeRange(1, 1000000, SoftMin = 1, SoftMax = 100)] int step = 1)
    {
        RequireList(list, "List.Slice");
        if (step < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "List.Slice requires a step of at least 1.");
        }

        var from = start < 0 ? Math.Max(0, list.Count + start) : Math.Min(start, list.Count);
        var stop = end ?? list.Count;
        var to = stop < 0 ? Math.Max(0, list.Count + stop) : Math.Min(stop, list.Count);
        var output = new List<object?>();
        for (long i = from; i < to; i += step)
        {
            output.Add(list[(int)i]);
        }

        return output;
    }

    /// <summary>Chops a list into consecutive sublists of the given lengths.</summary>
    /// <param name="list">The list to chop.</param>
    /// <param name="lengths">Sublist length(s): one number chops evenly; a list of numbers is applied in sequence and repeats until the list is used up.</param>
    /// <returns>The sublists (the last one may be shorter).</returns>
    [NodeName("List.Chop")]
    [return: NodeName("lists")]
    [NodeDescription("Chops a list into consecutive sublists: one length chops evenly ([1..7] by 3 → [1,2,3],[4,5,6],[7]); a list of lengths is applied in sequence and repeats until the input runs out (Dynamo behavior). Every length must be a whole number of at least 1.")]
    [NodeSearchTags("chop", "split", "partition", "chunk", "sublists", "group")]
    public static IList<object?> Chop(IList<object?> list, IList<object?> lengths)
    {
        RequireList(list, "List.Chop");
        if (lengths == null || lengths.Count == 0)
        {
            throw new ArgumentException("List.Chop requires at least one sublist length. Wire a number (or a list of numbers) into the 'lengths' input.", nameof(lengths));
        }

        var sizes = new List<int>(lengths.Count);
        for (var i = 0; i < lengths.Count; i++)
        {
            var length = lengths[i];
            if (length is IList && !(length is string))
            {
                throw new ArgumentException(
                    "List.Chop: length " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is a list, but every length must be one whole number. Flatten the 'lengths' list first (List.Flatten).", nameof(lengths));
            }

            if (length == null || !TypeCoercion.TryCoerce(length, typeof(int), out var coerced) || !(coerced is int size))
            {
                throw new ArgumentException(
                    "List.Chop: length " + (i + 1).ToString(CultureInfo.InvariantCulture) + " (" + (length == null ? "empty" : "'" + TypeCoercion.FormatValue(length) + "'") +
                    ") is not a whole number. Every length must be a number of at least 1.", nameof(lengths));
            }

            if (size < 1)
            {
                throw new ArgumentException(
                    "List.Chop: length " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is " + size.ToString(CultureInfo.InvariantCulture) + "; every length must be at least 1.", nameof(lengths));
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
    /// <param name="amount">How many copies to chain (≥ 0); the result may hold at most one million items.</param>
    /// <returns>The list repeated end-to-end.</returns>
    [NodeName("List.Cycle")]
    [return: NodeName("list")]
    [NodeDescription("Repeats the whole list a number of times, end-to-end ([a,b] × 3 → [a,b,a,b,a,b]). The result may hold at most 1,000,000 items; more is an error.")]
    [NodeSearchTags("cycle", "repeat", "tile", "loop", "duplicate")]
    public static IList<object?> Cycle(IList<object?> list, [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 100)] int amount)
    {
        RequireList(list, "List.Cycle");
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "List.Cycle requires a non-negative amount.");
        }

        RequireSize((long)list.Count * amount, "List.Cycle");
        var output = new List<object?>(list.Count * amount);
        if (list.Count == 0)
        {
            return output;
        }

        for (int i = 0; i < amount; i++)
        {
            output.AddRange(list);
        }

        return output;
    }

    /// <summary>A list made of one value repeated.</summary>
    /// <param name="item">The value to repeat.</param>
    /// <param name="amount">How many copies (≥ 0, at most one million).</param>
    /// <returns>The repeated-value list.</returns>
    [NodeName("List.OfRepeatedItem")]
    [return: NodeName("list")]
    [NodeDescription("A list of one value repeated N times — constant columns for tables, or a fixed pairing partner under Longest lacing. At most 1,000,000 copies; more is an error.")]
    [NodeSearchTags("repeat", "repeated", "fill", "constant", "duplicate")]
    public static IList<object?> OfRepeatedItem(object? item, [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 100)] int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "List.OfRepeatedItem requires a non-negative amount.");
        }

        RequireSize(amount, "List.OfRepeatedItem");
        var output = new List<object?>(amount);
        for (int i = 0; i < amount; i++)
        {
            output.Add(item);
        }

        return output;
    }

    /// <summary>Refuses a list that would be larger than <see cref="MaxListSize"/> items, with a sentence that says what to do.</summary>
    private static void RequireSize(long size, string nodeName)
    {
        if (size > MaxListSize)
        {
            throw new ArgumentException(
                nodeName + " would make " + size.ToString("N0", CultureInfo.InvariantCulture) + " items; the limit is " + MaxListSize.ToString("N0", CultureInfo.InvariantCulture) +
                ". Use a smaller amount (the limit protects the memory of Navisworks).");
        }
    }

    /// <summary>The largest element of a list.</summary>
    /// <param name="list">The list to scan (nulls and empty text are ignored); with none left the result is empty and the node warns.</param>
    /// <returns>The maximum element.</returns>
    [NodeName("List.MaximumItem")]
    [return: NodeName("item")]
    [NodeDescription("The largest element of a list (numbers, texts or dates). Items are compared as in List.Sort: numbers by value, text ignoring case (\"10\" and \"9\" are text, so \"9\" is the larger; List.Statistics reads such text as numbers); nulls and empty text are ignored. A list with nothing to compare gives an empty result and a warning, not an error.")]
    [NodeSearchTags("maximum", "max", "largest", "biggest", "highest")]
    public static object? MaximumItem([MultiInput] IList<object?> list)
    {
        return Extreme(list, "List.MaximumItem", larger: true);
    }

    /// <summary>The smallest element of a list.</summary>
    /// <param name="list">The list to scan (nulls and empty text are ignored); with none left the result is empty and the node warns.</param>
    /// <returns>The minimum element.</returns>
    [NodeName("List.MinimumItem")]
    [return: NodeName("item")]
    [NodeDescription("The smallest element of a list (numbers, texts or dates). Items are compared as in List.Sort: numbers by value, text ignoring case (\"10\" and \"9\" are text, so \"10\" is the smaller; List.Statistics reads such text as numbers); nulls and empty text are ignored. A list with nothing to compare gives an empty result and a warning, not an error.")]
    [NodeSearchTags("minimum", "min", "smallest", "lowest")]
    public static object? MinimumItem([MultiInput] IList<object?> list)
    {
        return Extreme(list, "List.MinimumItem", larger: false);
    }

    /// <summary>The distinct elements present in either list.</summary>
    /// <param name="list1">The first list.</param>
    /// <param name="list2">The second list.</param>
    /// <returns>The union, first-seen order, duplicates removed.</returns>
    [NodeName("List.SetUnion")]
    [return: NodeName("list")]
    [NodeDescription("The distinct elements present in EITHER list (first-seen order) — combine two item sets without duplicates. Values compare as in List.UniqueItems, lists and dictionaries by content.")]
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
    [NodeDescription("The distinct elements present in BOTH lists (ordered as in the first) — what two searches/sets have in common. Values compare as in List.UniqueItems, lists and dictionaries by content.")]
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
    [NodeDescription("The distinct elements of the FIRST list that are NOT in the second — subtract an ignore-list from a result set. Values compare as in List.UniqueItems, lists and dictionaries by content.")]
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
    /// <param name="offset">Elements to skip before counting starts.</param>
    /// <returns>The sampled elements.</returns>
    [NodeName("List.TakeEveryNthItem")]
    [return: NodeName("list")]
    [NodeDescription("Takes the n-th, 2n-th, 3n-th ... element, optionally after skipping offset elements (n = 3 takes the 3rd, 6th and 9th, which are the indices 2, 5 and 8). To keep the 1st, 4th, 7th ... instead, use List.Slice with a step.")]
    [NodeSearchTags("every", "nth", "sample", "skip", "thin", "step")]
    public static IList<object?> TakeEveryNthItem(
        IList<object?> list,
        [NodeRange(1, 1000000, SoftMin = 1, SoftMax = 100)] int n,
        [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 100)] int offset = 0)
    {
        RequireList(list, "List.TakeEveryNthItem");
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "List.TakeEveryNthItem requires n of at least 1.");
        }

        var output = new List<object?>();
        for (long i = (long)Math.Max(0, offset) + n - 1; i < list.Count; i += n)
        {
            output.Add(list[(int)i]);
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
    /// <param name="list">The booleans to test: only true counts as true; nulls and anything that is not true or false count as not-true, and a nested list is an error.</param>
    /// <returns>True when every element is true (and the list is not empty).</returns>
    [NodeName("List.AllTrue")]
    [return: NodeName("allTrue")]
    [NodeDescription("True when EVERY element of the list is true — collapse a mask into one verdict. Only the booleans true and false (or the text \"true\" and \"false\") count: an empty list gives false, null items count as not true, and any other value (a number, other text) counts as not true and adds a warning. A list inside the list is an error: set the input to @L2 for one verdict per sublist, or flatten it first. A wire that delivers nothing at all is ignored.")]
    [NodeSearchTags("all", "true", "every", "and", "mask", "verdict")]
    public static bool AllTrue([MultiInput] IList<object?> list)
    {
        RequireList(list, "List.AllTrue");
        var tally = new MaskTally(list.Count);
        var all = list.Count > 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (!ReadVerdictMask(list[i], i, "List.AllTrue", tally))
            {
                all = false;
            }
        }

        tally.Report();
        return all;
    }

    /// <summary>Whether any element of a boolean list is true.</summary>
    /// <param name="list">The booleans to test: only true counts as true; nulls and anything that is not true or false count as not-true, and a nested list is an error.</param>
    /// <returns>True when at least one element is true.</returns>
    [NodeName("List.AnyTrue")]
    [return: NodeName("anyTrue")]
    [NodeDescription("True when AT LEAST ONE element of the list is true — \"did anything match?\" in one node. Only the booleans true and false (or the text \"true\" and \"false\") count: an empty list gives false, null items count as not true, and any other value (a number, other text) counts as not true and adds a warning. A list inside the list is an error: set the input to @L2 for one answer per sublist, or flatten it first. A wire that delivers nothing at all is ignored.")]
    [NodeSearchTags("any", "true", "some", "or", "mask", "exists")]
    public static bool AnyTrue([MultiInput] IList<object?> list)
    {
        RequireList(list, "List.AnyTrue");
        var tally = new MaskTally(list.Count);
        var any = false;
        for (var i = 0; i < list.Count; i++)
        {
            if (ReadVerdictMask(list[i], i, "List.AnyTrue", tally))
            {
                any = true;
            }
        }

        tally.Report();
        return any;
    }

    /// <summary>How many elements of a boolean list are true (and how many are not).</summary>
    /// <param name="list">The booleans to count: only true counts as true; nulls and anything that is not true or false count as not-true, and a nested list is an error.</param>
    /// <returns>The true and not-true counts.</returns>
    [NodeName("List.CountTrue")]
    [MultiReturn("trueCount", "falseCount")]
    [PortKinds("integer", "integer")]
    [NodeDescription("Counts the true and not-true elements of a mask — \"37 of 340 matched\" for reports without filtering first. Only the booleans true and false (or the text \"true\" and \"false\") count as true or false: null items and any other value (a number, other text) go into the not-true count, and a value that is not a boolean adds a warning. A list inside the list is an error: set the input to @L2 for one count per sublist, or flatten it first. A wire that delivers nothing at all is ignored.")]
    [NodeSearchTags("count", "true", "false", "mask", "tally", "how many")]
    public static Dictionary<string, object> CountTrue([MultiInput] IList<object?> list)
    {
        RequireList(list, "List.CountTrue");
        var tally = new MaskTally(list.Count);
        int trueCount = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (ReadVerdictMask(list[i], i, "List.CountTrue", tally))
            {
                trueCount++;
            }
        }

        tally.Report();
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

    /// <summary>The largest/smallest element that is not empty, by the library's ordering rule (see List.Sort); null with a warning when there is none.</summary>
    private static object? Extreme(IList<object?> list, string nodeName, bool larger)
    {
        RequireList(list, nodeName);
        object? best = null;
        CellSortKey? bestKey = null;
        for (var i = 0; i < list.Count; i++)
        {
            var element = list[i];
            if (CellSortKey.IsEmpty(element))
            {
                continue;
            }

            RequireOrderable(element, i, nodeName);
            var key = CellSortKey.Of(element);
            if (bestKey == null)
            {
                best = element;
                bestKey = key;
                continue;
            }

            var comparison = CellSortKey.Compare(key, bestKey, descending: false);
            if (larger ? comparison > 0 : comparison < 0)
            {
                best = element;
                bestKey = key;
            }
        }

        if (bestKey == null)
        {
            NodeWarnings.Add(
                "The 'list' input has nothing to compare (it is empty, or holds only nulls and empty text), so there is no " +
                (larger ? "largest" : "smallest") + " item. The result is empty.");
        }

        return best;
    }

    /// <summary>
    /// Reads one entry of a mask that splits or cuts a list (List.FilterByBoolMask, List.TakeWhile, List.DropWhile): true or
    /// false, the text "true" or "false", or a number (0 is false); null counts as false. A list or dictionary, or a value
    /// that is not true or false, is an error that names the item.
    /// </summary>
    internal static bool ReadSplitMask(object? entry, int index, string nodeName)
    {
        if (entry == null)
        {
            return false;
        }

        if (entry is bool flag)
        {
            return flag;
        }

        RequireNotNested(entry, index, nodeName);
        if (TypeCoercion.TryCoerce(entry, typeof(bool), out var coerced) && coerced is bool converted)
        {
            return converted;
        }

        throw new ArgumentException(
            nodeName + ": mask item " + index.ToString(CultureInfo.InvariantCulture) + " ('" + TypeCoercion.FormatValue(entry) + "') is not true or false.");
    }

    // The verdict nodes (AllTrue, AnyTrue, CountTrue) are stricter than the nodes that split a list: a number is not a boolean.
    private static bool ReadVerdictMask(object? entry, int index, string nodeName, MaskTally tally)
    {
        if (entry == null)
        {
            return false;
        }

        if (entry is bool flag)
        {
            return flag;
        }

        RequireNotNested(entry, index, nodeName);
        if (entry is string text && bool.TryParse(text.Trim(), out var parsed))
        {
            return parsed;
        }

        tally.NotBoolean(index, entry);
        return false;
    }

    private static void RequireNotNested(object entry, int index, string nodeName)
    {
        if (entry is IList || entry is IDictionary)
        {
            throw new ArgumentException(
                nodeName + ": item " + index.ToString(CultureInfo.InvariantCulture) + " is " + (entry is IDictionary ? "a dictionary" : "a list") +
                ", not true or false. To work on each sublist set this input to @L2 (right-click, List Levels), or flatten the list first (List.Flatten).");
        }
    }

    /// <summary>Counts the entries of a mask that were not true or false, to report them in one warning.</summary>
    private sealed class MaskTally
    {
        private readonly int _count;
        private int _notBoolean;
        private int _firstIndex;
        private object? _firstValue;

        public MaskTally(int count)
        {
            _count = count;
        }

        public void NotBoolean(int index, object value)
        {
            if (_notBoolean++ == 0)
            {
                _firstIndex = index;
                _firstValue = value;
            }
        }

        public void Report()
        {
            if (_notBoolean > 0)
            {
                NodeWarnings.Add(
                    _notBoolean.ToString(CultureInfo.InvariantCulture) + " of " + _count.ToString(CultureInfo.InvariantCulture) +
                    " items are not true or false (the first is item " + _firstIndex.ToString(CultureInfo.InvariantCulture) + ", '" +
                    TypeCoercion.FormatValue(_firstValue) + "') and count as not true.");
            }
        }
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
    /// The positions of the items in sorted order, by the library's one ordering rule (<see cref="CellSortKey"/>): a stable sort,
    /// numbers by value, text ignoring case, empty items last in either direction. A list or a dictionary has no order.
    /// </summary>
    internal static List<int> OrderOf(IList<object?> items, bool descending, string nodeName)
    {
        var keys = new CellSortKey[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            RequireOrderable(items[i], i, nodeName);
            keys[i] = CellSortKey.Of(items[i]);
        }

        var comparer = CellSortKey.Comparer(descending);
        return Enumerable.Range(0, items.Count).OrderBy(i => keys[i], comparer).ToList();
    }

    /// <summary>Refuses a list or dictionary where a value that can be put in order is needed, with a sentence that says what to do.</summary>
    internal static void RequireOrderable(object? item, int index, string nodeName)
    {
        var kind = item is IDictionary ? "a dictionary" : item is IList && !(item is string) ? "a list" : null;
        if (kind != null)
        {
            throw new ArgumentException(
                nodeName + ": item " + index.ToString(CultureInfo.InvariantCulture) + " is " + kind + ", and " + kind + " cannot be put in order. " +
                (kind == "a list"
                    ? "To work on each sublist set this input to @L2 (right-click, List Levels), or flatten the list first (List.Flatten)."
                    : "Take one of its values first (Dictionary.ValueAtKey)."));
        }
    }

    /// <summary>Equality comparer delegating to <see cref="ValueComparison.AreEqual"/>.</summary>
    private sealed class NodeValueEqualityComparer : IEqualityComparer<object?>
    {
        public static readonly NodeValueEqualityComparer Instance = new NodeValueEqualityComparer();

        public new bool Equals(object? x, object? y) => ValueComparison.AreEqual(x, y);

        public int GetHashCode(object? obj) => ValueComparison.GetValueHashCode(obj);
    }
}
