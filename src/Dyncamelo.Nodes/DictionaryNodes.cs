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
}
