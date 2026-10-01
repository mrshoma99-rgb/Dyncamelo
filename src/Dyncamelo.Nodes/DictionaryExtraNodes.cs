using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// More dictionary nodes: key tests, removing and merging, conversion to and from rows, defaults and inversion.
/// Dyncamelo dictionaries are string-keyed; keys of other types are matched and returned as their invariant text,
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [return: NodeName("hasKey")]
    [NodeDescription("Tests whether a dictionary has the given key (case-sensitive).")]
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
    [NodeDescription("Returns a copy of the dictionary without the given key (a missing key is fine).")]
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
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
    [NodeDescription("Returns the value stored under a key, or a default value when the key is missing.")]
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

    // ── Helpers (private, so the loader does not import them) ───────────────

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

    // Finds a key the way the other dictionary nodes name keys: a string key directly, any other key type by its
    // invariant text.
    private static bool TryGetValue(IDictionary dictionary, string key, out object? value)
    {
        if (dictionary.Contains(key))
        {
            value = dictionary[key];
            return true;
        }

        foreach (DictionaryEntry entry in dictionary)
        {
            if (!(entry.Key is string) && string.Equals(TypeCoercion.FormatValue(entry.Key), key, StringComparison.Ordinal))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
