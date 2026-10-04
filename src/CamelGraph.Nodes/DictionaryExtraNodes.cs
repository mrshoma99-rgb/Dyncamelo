using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using CamelGraph.Core.Loader;
using CamelGraph.Core.Types;

namespace CamelGraph.Nodes;

/// <summary>
/// More dictionary nodes: key tests, removing and merging, conversion to and from rows, defaults and inversion.
/// CamelGraph dictionaries are string-keyed; keys of other types are matched and returned as their invariant text,
/// the way <see cref="DictionaryNodes"/> does. Every node that changes a dictionary returns a new one and leaves
/// its input untouched.
/// </summary>
[NodeCategory("Dictionary")]
public static class DictionaryExtraNodes
{
    /// <summary>Tests whether a dictionary has a key (case-sensitive).</summary>
    /// <param name="dictionary">The dictionary to look in.</param>
    /// <param name="key">The key to look for.</param>
    /// <returns>True when the dictionary has the key.</returns>
    [NodeName("Dictionary.ContainsKey")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("hasKey")]
    [NodeDescription("Tests whether a dictionary has the given key (case-sensitive)."
        + " A list of dictionaries and a list of keys on separate inputs pair up item by item and stop at the shorter list (the default Shortest lacing); set Cross-Product lacing (right-click the node) to use every key with every dictionary.")]
    [NodeSearchTags("has", "exists", "key", "lookup", "contains", "map")]
    public static bool ContainsKey(IDictionary dictionary, string key)
    {
        RequireDictionary("Dictionary.ContainsKey", dictionary);
        RequireKey("Dictionary.ContainsKey", key);
        return TryGetValue(dictionary, key, out _);
    }

    /// <summary>Copies a dictionary without one key. A key that is not there is fine: the copy is then complete.</summary>
    /// <param name="dictionary">The dictionary to copy.</param>
    /// <param name="key">The key to leave out.</param>
    /// <returns>A new dictionary without the key.</returns>
    [NodeName("Dictionary.RemoveKey")]
    [return: NodeName("dictionary")]
    [NodeDescription("Returns a copy of the dictionary without the given key (a missing key is fine)."
        + " A list of dictionaries and a list of keys on separate inputs pair up item by item and stop at the shorter list (the default Shortest lacing); set Cross-Product lacing (right-click the node) to use every key with every dictionary. To leave out several keys of one dictionary use Dictionary.RemoveKeys.")]
    [NodeSearchTags("delete", "drop", "remove", "omit", "without", "map")]
    public static Dictionary<string, object?> RemoveKey(IDictionary dictionary, string key)
    {
        RequireDictionary("Dictionary.RemoveKey", dictionary);
        RequireKey("Dictionary.RemoveKey", key);

        var copy = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            var entryKey = TypeCoercion.FormatValue(entry.Key);
            if (!string.Equals(entryKey, key, StringComparison.Ordinal))
            {
                copy[entryKey] = entry.Value;
            }
        }

        return copy;
    }

    /// <summary>
    /// Combines dictionaries into one. When several have the same key the value from the LAST dictionary (in the order
    /// they are wired or listed) wins. The inputs are not modified.
    /// </summary>
    /// <param name="dictionaries">The dictionaries to combine, earliest first.</param>
    /// <returns>A new dictionary with the keys of all of them.</returns>
    [NodeName("Dictionary.Merge")]
    [return: NodeName("dictionary")]
    [NodeDescription("Combines several dictionaries into a new one; for a repeated key the later dictionary wins.")]
    [NodeSearchTags("combine", "join", "union", "overlay", "update", "defaults", "map")]
    public static Dictionary<string, object?> Merge([MultiInput] IList<object?> dictionaries)
    {
        if (dictionaries == null)
        {
            throw new ArgumentNullException(nameof(dictionaries), "Dictionary.Merge requires dictionaries to combine. Wire them into the 'dictionaries' input.");
        }

        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (int i = 0; i < dictionaries.Count; i++)
        {
            var item = dictionaries[i];
            if (item is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    merged[TypeCoercion.FormatValue(entry.Key)] = entry.Value;
                }

                continue;
            }

            throw new ArgumentException(
                "Dictionary.Merge: item " + i.ToString(CultureInfo.InvariantCulture) + " is " +
                (item == null ? "null" : "a " + item.GetType().Name) + ", not a dictionary. " +
                "Only dictionaries can be merged (items are counted from 0).",
                nameof(dictionaries));
        }

        return merged;
    }

    /// <summary>Number of entries in a dictionary.</summary>
    /// <param name="dictionary">The dictionary to measure.</param>
    /// <returns>The number of keys.</returns>
    [NodeName("Dictionary.Count")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [return: NodeName("count")]
    [NodeDescription("Returns the number of entries in a dictionary.")]
    [NodeSearchTags("size", "length", "number", "entries", "keys", "map")]
    public static int Count(IDictionary dictionary)
    {
        RequireDictionary("Dictionary.Count", dictionary);
        return dictionary.Count;
    }

    /// <summary>
    /// Builds a dictionary from rows that are [key, value] pairs, e.g. the rows of a table's two columns. Keys are
    /// converted to text; extra cells after the first two are ignored and a repeated key keeps the last value.
    /// </summary>
    /// <param name="rows">A list of rows, each a list with the key in cell 0 and the value in cell 1.</param>
    /// <returns>The new dictionary.</returns>
    [NodeName("Dictionary.FromRows")]
    [return: NodeName("dictionary")]
    [NodeDescription("Builds a dictionary from a list of [key, value] rows (a later row wins for a repeated key).")]
    [NodeSearchTags("pairs", "table", "rows", "keyvalue", "create", "columns", "map")]
    public static Dictionary<string, object?> FromRows(IList<object?> rows)
    {
        if (rows == null)
        {
            throw new ArgumentNullException(nameof(rows), "Dictionary.FromRows requires a list of [key, value] rows. Wire them into the 'rows' input.");
        }

        var dictionary = new Dictionary<string, object?>(rows.Count, StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i] as IList;
            if (row == null || row.Count < 2)
            {
                throw new ArgumentException(
                    "Dictionary.FromRows: row " + i.ToString(CultureInfo.InvariantCulture) + " must be a list with a key and a value, but " +
                    (rows[i] == null ? "it is null" : row == null ? "it is " + rows[i]!.GetType().Name : "it has " + row.Count.ToString(CultureInfo.InvariantCulture) + " cell(s)") +
                    " (rows are counted from 0).",
                    nameof(rows));
            }

            dictionary[TypeCoercion.FormatValue(row[0])] = row[1];
        }

        return dictionary;
    }

    /// <summary>The entries of a dictionary as rows, in the dictionary's storage order.</summary>
    /// <param name="dictionary">The dictionary to convert.</param>
    /// <returns>A list of [key, value] rows; the key is text.</returns>
    [NodeName("Dictionary.ToRows")]
    [return: NodeName("rows")]
    [NodeDescription("Converts a dictionary to a list of [key, value] rows.")]
    [NodeSearchTags("pairs", "table", "rows", "entries", "items", "keyvalue", "map")]
    public static IList<object?> ToRows(IDictionary dictionary)
    {
        RequireDictionary("Dictionary.ToRows", dictionary);

        var rows = new List<object?>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            rows.Add(new List<object?> { TypeCoercion.FormatValue(entry.Key), entry.Value });
        }

        return rows;
    }

    /// <summary>Looks up a key and falls back to a default when the key is missing (unlike Dictionary.ValueAtKey, which fails).</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <param name="key">The key to look up.</param>
    /// <param name="defaultValue">Returned when the key is missing (null when left unwired).</param>
    /// <returns>The stored value, or the default.</returns>
    [NodeName("Dictionary.ValueOrDefault")]
    [return: NodeName("value")]
    [NodeDescription("Returns the value stored under a key, or a default value when the key is missing."
        + " A list of dictionaries and a list of keys on separate inputs pair up item by item and stop at the shorter list (the default Shortest lacing); set Cross-Product lacing (right-click the node) to use every key with every dictionary.")]
    [NodeSearchTags("get", "lookup", "fallback", "safe", "optional", "missing", "map")]
    public static object? ValueOrDefault(IDictionary dictionary, string key, object? defaultValue = null)
    {
        RequireDictionary("Dictionary.ValueOrDefault", dictionary);
        RequireKey("Dictionary.ValueOrDefault", key);
        return TryGetValue(dictionary, key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Swaps keys and values: each value (shown as text, the way String.FromObject shows it) becomes a key and its
    /// old key becomes the value. When two entries have the same value the LAST one wins.
    /// </summary>
    /// <param name="dictionary">The dictionary to invert.</param>
    /// <returns>A new dictionary from value text to the old key.</returns>
    [NodeName("Dictionary.Invert")]
    [return: NodeName("dictionary")]
    [NodeDescription("Swaps keys and values into a new dictionary (values become text keys; for equal values the last entry wins).")]
    [NodeSearchTags("reverse", "swap", "flip", "reverse lookup", "map")]
    public static Dictionary<string, object?> Invert(IDictionary dictionary)
    {
        RequireDictionary("Dictionary.Invert", dictionary);

        var inverted = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            inverted[TypeCoercion.FormatValue(entry.Value)] = TypeCoercion.FormatValue(entry.Key);
        }

        return inverted;
    }

    /// <summary>Keeps only some keys of a dictionary, in the order the keys are listed.</summary>
    /// <param name="dictionary">The dictionary to copy from.</param>
    /// <param name="keys">The keys to keep (a single key works too). A key the dictionary does not have is left out; a key listed twice is kept once.</param>
    /// <returns>A new dictionary with only those keys.</returns>
    [NodeName("Dictionary.SelectKeys")]
    [return: NodeName("dictionary")]
    [NodeDescription("Returns a copy of the dictionary with only the listed keys, in the order they are listed (trim a property bag to the few keys a report needs). A key the dictionary does not have is left out. A list of dictionaries gives one trimmed dictionary each; the key list applies to every one of them.")]
    [NodeSearchTags("pick", "keep", "filter", "subset", "only", "columns", "fields", "map")]
    public static Dictionary<string, object?> SelectKeys(IDictionary dictionary, IList<object?> keys)
    {
        RequireDictionary("Dictionary.SelectKeys", dictionary);
        RequireKeys("Dictionary.SelectKeys", keys);

        var selected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var item in keys)
        {
            if (item == null)
            {
                continue;
            }

            var key = TypeCoercion.FormatValue(item);
            if (!selected.ContainsKey(key) && TryGetValue(dictionary, key, out var value))
            {
                selected[key] = value;
            }
        }

        return selected;
    }

    /// <summary>Copies a dictionary without several keys.</summary>
    /// <param name="dictionary">The dictionary to copy.</param>
    /// <param name="keys">The keys to leave out (a single key works too). A key the dictionary does not have is fine.</param>
    /// <returns>A new dictionary without those keys.</returns>
    [NodeName("Dictionary.RemoveKeys")]
    [return: NodeName("dictionary")]
    [NodeDescription("Returns a copy of the dictionary without any of the listed keys (a key it does not have is fine). A list of keys gives one dictionary without all of them, unlike Dictionary.RemoveKey, which pairs a list of keys with a list of dictionaries. A list of dictionaries gives one result each; the key list applies to every one of them.")]
    [NodeSearchTags("delete", "drop", "remove", "omit", "without", "many", "map")]
    public static Dictionary<string, object?> RemoveKeys(IDictionary dictionary, IList<object?> keys)
    {
        RequireDictionary("Dictionary.RemoveKeys", dictionary);
        RequireKeys("Dictionary.RemoveKeys", keys);

        var drop = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in keys)
        {
            if (item != null)
            {
                drop.Add(TypeCoercion.FormatValue(item));
            }
        }

        var copy = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            var entryKey = TypeCoercion.FormatValue(entry.Key);
            if (!drop.Contains(entryKey))
            {
                copy[entryKey] = entry.Value;
            }
        }

        return copy;
    }

    /// <summary>Sets several keys of a dictionary at once.</summary>
    /// <param name="dictionary">The dictionary to copy.</param>
    /// <param name="keys">The keys to set or update (converted to text).</param>
    /// <param name="values">One value per key, in the same order.</param>
    /// <returns>A new dictionary with all the keys set; for a key listed twice the last value wins.</returns>
    [NodeName("Dictionary.SetValues")]
    [return: NodeName("dictionary")]
    [NodeDescription("Returns a copy of the dictionary with several keys set or updated at once, from a list of keys and a list of values of the same length (the several-key version of Dictionary.SetValueAtKey). For a key listed twice the last value wins; the input dictionary is not changed. A list of dictionaries gives one result each; the keys and values apply to every one of them.")]
    [NodeSearchTags("set", "update", "insert", "put", "many", "multiple", "map")]
    public static Dictionary<string, object?> SetValues(IDictionary dictionary, IList<object?> keys, IList<object?> values)
    {
        RequireDictionary("Dictionary.SetValues", dictionary);
        RequireKeys("Dictionary.SetValues", keys);
        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "Dictionary.SetValues requires a list of values, one per key. Wire them into the 'values' input.");
        }

        if (keys.Count != values.Count)
        {
            throw new ArgumentException(
                "Dictionary.SetValues requires the same number of keys (" + keys.Count.ToString(CultureInfo.InvariantCulture) +
                ") and values (" + values.Count.ToString(CultureInfo.InvariantCulture) + ").");
        }

        var copy = new Dictionary<string, object?>(dictionary.Count + keys.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            copy[TypeCoercion.FormatValue(entry.Key)] = entry.Value;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            if (keys[i] == null)
            {
                throw new ArgumentException("Dictionary.SetValues: key " + (i + 1).ToString(CultureInfo.InvariantCulture) + " is empty; every value needs a key.");
            }

            copy[TypeCoercion.FormatValue(keys[i])] = values[i];
        }

        return copy;
    }

    /// <summary>Reads a value deep inside nested dictionaries and lists, such as the result of JSON.Parse or XML.Parse.</summary>
    /// <param name="value">The dictionary (or list) to read from.</param>
    /// <param name="path">The steps to follow, separated by / (or by . when the path has no /): a key such as Project/Name, a list position such as Tasks/Task/0/Name (negative counts from the end), or * for every item of a list.</param>
    /// <param name="defaultValue">Returned when any step of the path is missing (null when left unwired).</param>
    /// <returns>The value at the path, a list of the results when the path has a *, or the default.</returns>
    [NodeName("Dictionary.ValueAtPath")]
    [return: NodeName("value")]
    [NodeDescription("Follows a path into nested data (the result of JSON.Parse or XML.Parse) and returns what it finds there, or a default value when any step is missing. The steps are separated by / (or by . when the path has no /): a key (Project/Name), a position in a list (Tasks/Task/0/Name; -1 is the last item) or * for every item of a list (Tasks/Task/*/Name gives a list of names). A position of 0 or -1 also works on a value that is not a list, so the same path reads a file with one <Task> and a file with many. A list of paths gives one result per path.")]
    [NodeSearchTags("path", "nested", "deep", "json", "xml", "get", "lookup", "navigate", "drill", "map")]
    public static object? ValueAtPath(object? value, string path, object? defaultValue = null)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path), "Dictionary.ValueAtPath requires a path such as Project/Tasks/Task/0/Name. Wire text into the 'path' input.");
        }

        var separator = path.IndexOf('/') >= 0 ? '/' : '.';
        var steps = new List<string>();
        foreach (var step in path.Split(separator))
        {
            var trimmed = step.Trim();
            if (trimmed.Length > 0)
            {
                steps.Add(trimmed);
            }
        }

        var found = Follow(value, steps, 0, out var result);
        return found ? result : defaultValue;
    }

    // Walks one step at a time. A step is a key (of a dictionary), a position (of a list; 0 and -1 also read a value that is not a
    // list, as a one-item list), or * (every item of a list, the rest of the path applied to each).
    private static bool Follow(object? current, List<string> steps, int index, out object? result)
    {
        if (index == steps.Count)
        {
            result = current;
            return true;
        }

        var step = steps[index];
        if (step == "*")
        {
            var items = current is IList all && !(current is string) ? all : new List<object?> { current };
            var results = new List<object?>(items.Count);
            foreach (var item in items)
            {
                results.Add(Follow(item, steps, index + 1, out var each) ? each : null);
            }

            result = results;
            return true;
        }

        var isPosition = int.TryParse(step, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var position);
        if (current is IDictionary dictionary && TryGetValue(dictionary, step, out var inner))
        {
            return Follow(inner, steps, index + 1, out result);
        }

        if (current is IList list && !(current is string))
        {
            var effective = position < 0 ? list.Count + position : position;
            if (isPosition && effective >= 0 && effective < list.Count)
            {
                return Follow(list[effective], steps, index + 1, out result);
            }
        }
        else if (isPosition && (position == 0 || position == -1) && current != null)
        {
            return Follow(current, steps, index + 1, out result);
        }

        result = null;
        return false;
    }

    // ── Helpers (private, so the loader does not import them) ───────────────

    private static void RequireKeys(string node, IList<object?> keys)
    {
        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys), node + " requires a list of keys. Wire a key (or a list of keys) into the 'keys' input.");
        }
    }

    private static void RequireDictionary(string node, IDictionary dictionary)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), node + " requires a dictionary. Wire a dictionary into the 'dictionary' input.");
        }
    }

    private static void RequireKey(string node, string key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key), node + " requires a key. Wire text into the 'key' input.");
        }
    }

    // A dictionary type that can only hold string keys: it implements the generic dictionary interfaces and every one of them has string
    // as its key type (Dictionary<string, T>, SortedDictionary<string, T>, ConcurrentDictionary<string, T>, ...). Worked out once per type.
    private static readonly ConcurrentDictionary<Type, bool> StringKeyedTypes = new ConcurrentDictionary<Type, bool>();

    private static bool HoldsOnlyStringKeys(Type type) => StringKeyedTypes.GetOrAdd(type, IsStringKeyed);

    private static bool IsStringKeyed(Type type)
    {
        var generic = false;
        foreach (var contract in type.GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            var definition = contract.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                if (contract.GetGenericArguments()[0] != typeof(string))
                {
                    return false;
                }

                generic = true;
            }
        }

        return generic;
    }

    // Finds a key the way the other dictionary nodes name keys: a string key directly (by the dictionary's own rules for keys), any
    // other key type by its invariant text. A miss is the normal answer of ContainsKey and ValueOrDefault, so it has to be cheap: the
    // search for a key that is not a string looks at every entry, which is pointless (and was the whole cost of a miss) in a
    // dictionary that cannot hold anything but string keys, so only a dictionary that can hold other keys is searched.
    internal static bool TryGetValue(IDictionary dictionary, string key, out object? value)
    {
        if (dictionary.Contains(key))
        {
            value = dictionary[key];
            return true;
        }

        if (!HoldsOnlyStringKeys(dictionary.GetType()))
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!(entry.Key is string) && string.Equals(TypeCoercion.FormatValue(entry.Key), key, StringComparison.Ordinal))
                {
                    value = entry.Value;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
