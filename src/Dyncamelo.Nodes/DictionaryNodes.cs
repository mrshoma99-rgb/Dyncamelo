using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes;

/// <summary>
/// Dictionary nodes. Dyncamelo dictionaries are string-keyed; the engine
/// treats dictionaries as scalars, so lists of dictionaries replicate these
/// nodes element-wise.
/// </summary>
[NodeCategory("Dictionary")]
public static class DictionaryNodes
{
    /// <summary>
    /// Builds a dictionary by pairing a list of keys with a list of values.
    /// Keys are converted to strings; when a key occurs twice the last value
    /// wins. The two lists must have the same length.
    /// </summary>
    /// <param name="keys">The keys (converted to strings).</param>
    /// <param name="values">The values, one per key.</param>
    /// <returns>The new dictionary.</returns>
    [NodeName("Dictionary.ByKeysValues")]
    [return: NodeName("dictionary")]
    [NodeDescription("Creates a dictionary from a list of keys and a list of values of the same length.")]
    [NodeSearchTags("map", "hash", "keyvalue")]
    public static Dictionary<string, object?> ByKeysValues(IList<object?> keys, IList<object?> values)
    {
        if (keys == null)
        {
            throw new ArgumentNullException(nameof(keys), "Dictionary.ByKeysValues requires a list of keys.");
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values), "Dictionary.ByKeysValues requires a list of values.");
        }

        if (keys.Count != values.Count)
        {
            throw new ArgumentException(
                "Dictionary.ByKeysValues requires the same number of keys (" +
                keys.Count.ToString(CultureInfo.InvariantCulture) + ") and values (" +
                values.Count.ToString(CultureInfo.InvariantCulture) + ").");
        }

        var dictionary = new Dictionary<string, object?>(keys.Count, StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            dictionary[TypeCoercion.FormatValue(keys[i])] = values[i];
        }

        return dictionary;
    }

    /// <summary>Looks up the value stored under a key.</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value stored under the key.</returns>
    [NodeName("Dictionary.ValueAtKey")]
    [return: NodeName("value")]
    [NodeDescription("Returns the value stored under the given key.")]
    [NodeSearchTags("lookup", "get", "map")]
    public static object? ValueAtKey(IDictionary dictionary, string key)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), "Dictionary.ValueAtKey requires a dictionary.");
        }

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key), "Dictionary.ValueAtKey requires a key.");
        }

        if (dictionary.Contains(key))
        {
            return dictionary[key];
        }

        throw new KeyNotFoundException("Key '" + key + "' was not found in the dictionary.");
    }

    /// <summary>All keys of a dictionary, in the dictionary's storage order.</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <returns>The keys as a list of strings.</returns>
    [NodeName("Dictionary.Keys")]
    [return: NodeName("keys")]
    [NodeDescription("Returns all keys of a dictionary as a list.")]
    [NodeSearchTags("names", "fields", "map")]
    public static IList<string> Keys(IDictionary dictionary)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), "Dictionary.Keys requires a dictionary.");
        }

        var keys = new List<string>(dictionary.Count);
        foreach (var key in dictionary.Keys)
        {
            keys.Add(TypeCoercion.FormatValue(key));
        }

        return keys;
    }

    /// <summary>All values of a dictionary, in the dictionary's storage order.</summary>
    /// <param name="dictionary">The dictionary to read from.</param>
    /// <returns>The values as a list.</returns>
    [NodeName("Dictionary.Values")]
    [return: NodeName("values")]
    [NodeDescription("Returns all values of a dictionary as a list.")]
    [NodeSearchTags("entries", "contents", "map")]
    public static IList<object?> Values(IDictionary dictionary)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), "Dictionary.Values requires a dictionary.");
        }

        var values = new List<object?>(dictionary.Count);
        foreach (var value in dictionary.Values)
        {
            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Sets (or updates) the value stored under a key. Returns a new
    /// dictionary; the input dictionary is not modified.
    /// </summary>
    /// <param name="dictionary">The dictionary to copy.</param>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store under the key.</param>
    /// <returns>A new dictionary with the key set.</returns>
    [NodeName("Dictionary.SetValueAtKey")]
    [return: NodeName("dictionary")]
    [NodeDescription("Returns a copy of the dictionary with the given key set or updated.")]
    [NodeSearchTags("set", "update", "insert", "put", "map")]
    public static Dictionary<string, object?> SetValueAtKey(IDictionary dictionary, string key, object? value)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary), "Dictionary.SetValueAtKey requires a dictionary.");
        }

        if (key == null)
        {
            throw new ArgumentNullException(nameof(key), "Dictionary.SetValueAtKey requires a key.");
        }

        var copy = new Dictionary<string, object?>(dictionary.Count + 1, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            copy[TypeCoercion.FormatValue(entry.Key)] = entry.Value;
        }

        copy[key] = value;
        return copy;
    }
}
