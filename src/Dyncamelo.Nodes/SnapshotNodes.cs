using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;
using Newtonsoft.Json;

namespace Dyncamelo.Nodes;

/// <summary>
/// Snapshot nodes: diffing GUID-keyed dictionaries persisted between runs —
/// the engine behind model-version compare and clash-delta reports
/// (snapshot A → JSON.WriteToFile, later JSON.ReadFromFile → Snapshot.Diff).
/// </summary>
[NodeCategory("File")]
public static class SnapshotNodes
{
    /// <summary>
    /// Diffs two dictionaries by key (typically element GUID → properties):
    /// keys only in <paramref name="newValue"/> are "added", keys only in
    /// <paramref name="oldValue"/> are "removed", and keys present in both
    /// whose values differ are "changed". Values are compared by JSON
    /// equality — nested dictionaries compare equal regardless of key order,
    /// while list order matters. Key lists keep their dictionary's order.
    /// </summary>
    /// <param name="oldValue">The older snapshot dictionary.</param>
    /// <param name="newValue">The newer snapshot dictionary.</param>
    /// <returns>Dictionary with "addedKeys", "removedKeys" and "changedKeys".</returns>
    [NodeName("Snapshot.Diff")]
    [MultiReturn("addedKeys", "removedKeys", "changedKeys")]
    [NodeDescription("Diffs two GUID-keyed dictionaries: added/removed/changed keys (values compared by JSON equality; nested dictionary key order is ignored).")]
    [NodeSearchTags("compare", "delta", "difference", "version", "changes", "model compare")]
    public static Dictionary<string, object> Diff(IDictionary oldValue, IDictionary newValue)
    {
        if (oldValue == null)
        {
            throw new ArgumentNullException(nameof(oldValue), "Snapshot.Diff requires the old snapshot dictionary.");
        }

        if (newValue == null)
        {
            throw new ArgumentNullException(nameof(newValue), "Snapshot.Diff requires the new snapshot dictionary.");
        }

        var oldMap = ToOrderedMap(oldValue);
        var newMap = ToOrderedMap(newValue);

        var addedKeys = new List<string>();
        var removedKeys = new List<string>();
        var changedKeys = new List<string>();

        foreach (var key in oldMap.Order)
        {
            if (!newMap.Values.ContainsKey(key))
            {
                removedKeys.Add(key);
            }
        }

        foreach (var key in newMap.Order)
        {
            if (!oldMap.Values.TryGetValue(key, out var oldEntry))
            {
                addedKeys.Add(key);
            }
            else if (!string.Equals(CanonicalJson(oldEntry), CanonicalJson(newMap.Values[key]), StringComparison.Ordinal))
            {
                changedKeys.Add(key);
            }
        }

        return new Dictionary<string, object>
        {
            ["addedKeys"] = addedKeys,
            ["removedKeys"] = removedKeys,
            ["changedKeys"] = changedKeys,
        };
    }

    // ------------------------------------------------------------------
    // Helpers (not imported as nodes: non-public).
    // ------------------------------------------------------------------

    private sealed class OrderedMap
    {
        internal OrderedMap(List<string> order, Dictionary<string, object?> values)
        {
            Order = order;
            Values = values;
        }

        internal List<string> Order { get; }
        internal Dictionary<string, object?> Values { get; }
    }

    private static OrderedMap ToOrderedMap(IDictionary dictionary)
    {
        var order = new List<string>(dictionary.Count);
        var values = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            var key = TypeCoercion.FormatValue(entry.Key);
            if (!values.ContainsKey(key))
            {
                order.Add(key);
            }

            values[key] = entry.Value;
        }

        return new OrderedMap(order, values);
    }

    /// <summary>
    /// Canonical JSON form of a value: dictionaries with keys sorted
    /// ordinally (so key order never causes a false "changed"), lists in
    /// order, scalars via Newtonsoft's invariant serialization.
    /// </summary>
    private static string CanonicalJson(object? value)
    {
        var builder = new StringBuilder();
        AppendCanonicalJson(builder, value);
        return builder.ToString();
    }

    private static void AppendCanonicalJson(StringBuilder builder, object? value)
    {
        if (value is IDictionary dictionary)
        {
            var pairs = new List<KeyValuePair<string, object?>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                pairs.Add(new KeyValuePair<string, object?>(TypeCoercion.FormatValue(entry.Key), entry.Value));
            }

            pairs.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            builder.Append('{');
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(JsonConvert.SerializeObject(pairs[i].Key)).Append(':');
                AppendCanonicalJson(builder, pairs[i].Value);
            }

            builder.Append('}');
        }
        else if (value is IEnumerable enumerable && !(value is string))
        {
            builder.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                AppendCanonicalJson(builder, item);
                first = false;
            }

            builder.Append(']');
        }
        else
        {
            builder.Append(JsonConvert.SerializeObject(value));
        }
    }
}
