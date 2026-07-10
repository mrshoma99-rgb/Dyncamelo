using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Dyncamelo.Navisworks.Internal;

// ---------------------------------------------------------------------------
// Pure clash snapshot logic: the JSON file format persisted by
// Clash.SnapshotToFile and the new/resolved/persisting delta computed by
// Clash.CompareSnapshots. Deliberately free of Autodesk types so the matcher
// runs headlessly (CI-style weekly jobs, Linux harness); the node layer
// (ClashDeltaNodes.cs) owns every Navisworks call.
// ---------------------------------------------------------------------------

/// <summary>One clash result captured in a snapshot file.</summary>
internal sealed class ClashSnapshotEntry
{
    /// <summary>Display name of the owning clash test.</summary>
    [JsonProperty("test")]
    public string Test = string.Empty;

    /// <summary>
    /// Stable identity key: test name + the order-independent pair of item
    /// identities (see <see cref="ClashSnapshotFile.MakeKey"/>).
    /// </summary>
    [JsonProperty("key")]
    public string Key = string.Empty;

    [JsonProperty("name")]
    public string Name = string.Empty;

    [JsonProperty("status")]
    public string Status = string.Empty;

    /// <summary>Group display name, "" for ungrouped results.</summary>
    [JsonProperty("group")]
    public string Group = string.Empty;

    /// <summary>Clash distance in document units.</summary>
    [JsonProperty("distance")]
    public double Distance;

    /// <summary>Clash point [x, y, z] in document units.</summary>
    [JsonProperty("center")]
    public double[] Center = new double[3];

    /// <summary>Item 1 identity ("guid:..." or "path:...").</summary>
    [JsonProperty("item1Id")]
    public string Item1Id = string.Empty;

    [JsonProperty("item2Id")]
    public string Item2Id = string.Empty;

    /// <summary>Human-readable selection-tree path of item 1.</summary>
    [JsonProperty("item1Path")]
    public string Item1Path = string.Empty;

    [JsonProperty("item2Path")]
    public string Item2Path = string.Empty;
}

/// <summary>Root shape of a clash snapshot JSON file.</summary>
internal sealed class ClashSnapshotRoot
{
    internal const string FormatName = "dyncamelo-clash-snapshot";

    [JsonProperty("format")]
    public string Format = FormatName;

    [JsonProperty("version")]
    public int Version = 1;

    [JsonProperty("createdUtc")]
    public DateTime CreatedUtc = DateTime.UtcNow;

    [JsonProperty("document")]
    public string DocumentFileName = string.Empty;

    [JsonProperty("results")]
    public List<ClashSnapshotEntry> Results = new List<ClashSnapshotEntry>();
}

/// <summary>Snapshot file IO plus the new/resolved/persisting delta matcher.</summary>
internal static class ClashSnapshotFile
{
    /// <summary>
    /// The order-independent identity of one clash: the test name plus the two
    /// item identities sorted, so a run that swaps item1/item2 still matches.
    /// </summary>
    internal static string MakeKey(string testName, string item1Id, string item2Id)
    {
        var first = item1Id ?? string.Empty;
        var second = item2Id ?? string.Empty;
        if (string.CompareOrdinal(first, second) > 0)
        {
            var swap = first;
            first = second;
            second = swap;
        }

        return (testName ?? string.Empty) + "|" + first + "|" + second;
    }

    /// <summary>Writes a snapshot file (creates the parent directory when missing).</summary>
    internal static void Write(string path, ClashSnapshotRoot root)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("No file path provided.", nameof(path));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(root, Formatting.Indented);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    /// <summary>Reads a snapshot file with clear errors for wrong files.</summary>
    internal static ClashSnapshotRoot Read(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("No file path provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Snapshot file not found: '" + path + "'.", path);
        }

        // Populate a blank-format instance so a foreign JSON file (which lacks
        // the "format" marker) cannot inherit it from the field initializer.
        var root = new ClashSnapshotRoot { Format = string.Empty, Results = new List<ClashSnapshotEntry>() };
        try
        {
            JsonConvert.PopulateObject(File.ReadAllText(path), root);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "'" + path + "' is not valid JSON (" + ex.Message + ").", ex);
        }

        if (!string.Equals(root.Format, ClashSnapshotRoot.FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "'" + path + "' is not a Dyncamelo clash snapshot (expected \"format\": \"" +
                ClashSnapshotRoot.FormatName + "\" — write it with Clash.SnapshotToFile).");
        }

        root.Results = root.Results ?? new List<ClashSnapshotEntry>();
        return root;
    }

    /// <summary>
    /// Diffs two snapshots by identity key. "New" = only in the new snapshot,
    /// "resolved" = only in the old one (the clash disappeared), "persisting" =
    /// present in both (reported with both statuses so status-only changes are
    /// visible). Duplicate keys inside one snapshot are matched pairwise by
    /// position.
    /// </summary>
    internal static void Compare(
        ClashSnapshotRoot oldRoot,
        ClashSnapshotRoot newRoot,
        out List<Dictionary<string, object?>> newResults,
        out List<Dictionary<string, object?>> resolved,
        out List<Dictionary<string, object?>> persisting)
    {
        var oldByKey = CountByKey(oldRoot.Results);
        var newByKey = CountByKey(newRoot.Results);
        var oldStatusByKey = FirstStatusByKey(oldRoot.Results);

        newResults = new List<Dictionary<string, object?>>();
        resolved = new List<Dictionary<string, object?>>();
        persisting = new List<Dictionary<string, object?>>();

        // Track how many entries of each key have been consumed as "persisting"
        // so duplicate keys (identical item pairs clashing twice) match 1:1.
        var consumed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in newRoot.Results)
        {
            consumed.TryGetValue(entry.Key, out var used);
            oldByKey.TryGetValue(entry.Key, out var available);
            if (used < available)
            {
                consumed[entry.Key] = used + 1;
                var row = ToDictionary(entry);
                row["previousStatus"] = oldStatusByKey.TryGetValue(entry.Key, out var oldStatus)
                    ? oldStatus
                    : string.Empty;
                persisting.Add(row);
            }
            else
            {
                newResults.Add(ToDictionary(entry));
            }
        }

        var resolvedConsumed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in oldRoot.Results)
        {
            resolvedConsumed.TryGetValue(entry.Key, out var used);
            newByKey.TryGetValue(entry.Key, out var available);
            resolvedConsumed[entry.Key] = used + 1;
            if (used >= available)
            {
                resolved.Add(ToDictionary(entry));
            }
        }
    }

    /// <summary>Node-port shape of one snapshot entry.</summary>
    internal static Dictionary<string, object?> ToDictionary(ClashSnapshotEntry entry)
    {
        return new Dictionary<string, object?>
        {
            ["test"] = entry.Test,
            ["name"] = entry.Name,
            ["status"] = entry.Status,
            ["group"] = entry.Group,
            ["distance"] = entry.Distance,
            ["center"] = new List<double>(entry.Center ?? new double[3]),
            ["item1Id"] = entry.Item1Id,
            ["item2Id"] = entry.Item2Id,
            ["item1Path"] = entry.Item1Path,
            ["item2Path"] = entry.Item2Path,
            ["key"] = entry.Key,
        };
    }

    private static Dictionary<string, int> CountByKey(IEnumerable<ClashSnapshotEntry> entries)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            counts.TryGetValue(entry.Key, out var count);
            counts[entry.Key] = count + 1;
        }

        return counts;
    }

    private static Dictionary<string, string> FirstStatusByKey(IEnumerable<ClashSnapshotEntry> entries)
    {
        var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!statuses.ContainsKey(entry.Key))
            {
                statuses[entry.Key] = entry.Status ?? string.Empty;
            }
        }

        return statuses;
    }
}
