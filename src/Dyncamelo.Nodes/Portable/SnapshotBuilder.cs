using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes.Portable;

/// <summary>
/// Builds the dictionary Model.Snapshot returns, in the shape Snapshot.Diff compares: one entry per item, keyed by
/// the item's instance GUID text, whose value is a dictionary of column header to property value. Items with no GUID
/// are keyed by their selection-tree path, and a key that is already taken gets " #2", " #3" ... so no item is lost.
/// Pure (no Navisworks types), so the keying is unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class SnapshotBuilder
{
    /// <summary>The prefix of the key of an item that has no instance GUID.</summary>
    public const string PathKeyPrefix = "path:";

    private readonly Dictionary<string, object?> _snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The snapshot built so far (GUID text to property dictionary).</summary>
    public Dictionary<string, object?> Snapshot => _snapshot;

    /// <summary>Adds one item.</summary>
    /// <param name="instanceGuid">The item's instance GUID (empty when it has none).</param>
    /// <param name="path">Gives the item's selection-tree path; only called when the GUID is empty.</param>
    /// <param name="headers">The column headers.</param>
    /// <param name="values">The values, one per header.</param>
    /// <returns>The key the item was stored under.</returns>
    public string Add(Guid instanceGuid, Func<string> path, IList<string> headers, IList<object?> values)
    {
        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        if (values == null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        if (headers.Count != values.Count)
        {
            throw new ArgumentException("There must be one value per header.", nameof(values));
        }

        var key = MakeKey(instanceGuid, path, _snapshot);
        var properties = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++)
        {
            properties[headers[i]] = Normalize(values[i]);
        }

        _snapshot[key] = properties;
        return key;
    }

    /// <summary>
    /// The key for an item: its GUID as lower-case hyphenated text, or "path:" and its path when it has no GUID;
    /// when that key is already in use, " #2", " #3" ... is appended.
    /// </summary>
    /// <param name="instanceGuid">The item's instance GUID (empty when it has none).</param>
    /// <param name="path">Gives the item's path; only called when the GUID is empty.</param>
    /// <param name="used">The snapshot being built (its keys are the taken ones).</param>
    /// <returns>A key that is not in <paramref name="used"/>.</returns>
    public static string MakeKey(Guid instanceGuid, Func<string>? path, IDictionary<string, object?> used)
    {
        if (used == null)
        {
            throw new ArgumentNullException(nameof(used));
        }

        var baseKey = instanceGuid != Guid.Empty
            ? instanceGuid.ToString("D", CultureInfo.InvariantCulture)
            : PathKeyPrefix + (path == null ? string.Empty : path());
        var key = baseKey;
        var suffix = 2;
        while (used.ContainsKey(key))
        {
            key = baseKey + " #" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        return key;
    }

    /// <summary>
    /// A property value in a form that compares and saves reliably: null, booleans, whole and decimal numbers, text
    /// and dates stay as they are; anything else (points, lists ...) becomes its invariant text.
    /// </summary>
    /// <param name="value">The value read from the model.</param>
    /// <returns>The value to store in the snapshot.</returns>
    public static object? Normalize(object? value)
    {
        switch (value)
        {
            case null:
            case string _:
            case bool _:
            case int _:
            case long _:
            case double _:
            case DateTime _:
                return value;
            case float single:
                return (double)single;
            case decimal number:
                return (double)number;
            case short small:
                return (int)small;
            case byte tiny:
                return (int)tiny;
            default:
                return TypeCoercion.FormatValue(value);
        }
    }
}
