using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Clash-run snapshots and the between-runs delta report (WS-E, Clash-Sloth
/// style): persist each run as JSON, then diff two files into new / resolved /
/// persisting clashes. The compare half is pure file IO, so it also runs
/// headless (weekly CI-style jobs via the CLI).
/// </summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashDeltaNodes
{
    /// <summary>Persists a clash-run snapshot to a JSON file.</summary>
    /// <param name="filePath">Destination .json path; the directory is created when missing.</param>
    /// <param name="tests">The tests to snapshot (empty/unwired = every test in the document).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written path and the number of results captured.</returns>
    [NodeName("Clash.SnapshotToFile")]
    [NodeDescription("Saves a clash-run snapshot (per result: test, item identities, status, distance, clash point) as JSON — one half of the between-runs delta report. Items are identified by InstanceGuid when available, else by their tree path.")]
    [NodeSearchTags("clash", "snapshot", "save", "history", "delta", "baseline", "json")]
    [MultiReturn("filePath", "resultCount")]
    public static Dictionary<string, object?> SnapshotToFile(
        string filePath,
        IEnumerable<ClashTest>? tests = null,
        Document? document = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var testList = new List<ClashTest>();
        if (tests != null)
        {
            foreach (var test in tests)
            {
                if (test != null)
                {
                    testList.Add(test);
                }
            }
        }

        if (testList.Count == 0)
        {
            testList = NavisValues.FlattenSavedItems<ClashTest>(clash.TestsData.Tests);
        }

        var root = new ClashSnapshotRoot
        {
            CreatedUtc = DateTime.UtcNow,
            DocumentFileName = doc.FileName ?? string.Empty,
        };

        foreach (var test in testList)
        {
            var testName = test.DisplayName ?? string.Empty;
            var results = new List<ClashResult>();
            var groupNames = new List<string>();
            ClashHelpers.FlattenResultsWithGroups(test, results, groupNames);
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var item1Id = ItemIdentity(result.Item1);
                var item2Id = ItemIdentity(result.Item2);
                var center = result.Center;
                root.Results.Add(new ClashSnapshotEntry
                {
                    Test = testName,
                    Key = ClashSnapshotFile.MakeKey(testName, item1Id, item2Id),
                    Name = result.DisplayName ?? string.Empty,
                    Status = result.Status.ToString(),
                    Group = groupNames[i],
                    Distance = result.Distance,
                    Center = center == null
                        ? new double[3]
                        : new[] { center.X, center.Y, center.Z },
                    Item1Id = item1Id,
                    Item2Id = item2Id,
                    Item1Path = NavisValues.ItemPath(result.Item1),
                    Item2Path = NavisValues.ItemPath(result.Item2),
                });
            }
        }

        ClashSnapshotFile.Write(filePath, root);
        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["resultCount"] = root.Results.Count,
        };
    }

    /// <summary>Diffs two clash snapshots into new / resolved / persisting clashes.</summary>
    /// <param name="oldPath">The older snapshot .json (the baseline).</param>
    /// <param name="newPath">The newer snapshot .json.</param>
    /// <returns>Result dictionaries per bucket plus a counts dictionary.</returns>
    [NodeName("Clash.CompareSnapshots")]
    [NodeDescription("Diffs two clash snapshots: clashes NEW since the baseline, clashes RESOLVED (disappeared), and clashes PERSISTING in both (with their previous status) — the weekly delta report no plugin does via live API. Pure file IO: needs no open model.")]
    [NodeSearchTags("clash", "compare", "delta", "diff", "new", "resolved", "persisting", "report")]
    [MultiReturn("newResults", "resolved", "persisting", "counts")]
    public static Dictionary<string, object?> CompareSnapshots(string oldPath, string newPath)
    {
        var oldRoot = ClashSnapshotFile.Read(oldPath);
        var newRoot = ClashSnapshotFile.Read(newPath);

        ClashSnapshotFile.Compare(oldRoot, newRoot, out var newResults, out var resolved, out var persisting);

        return new Dictionary<string, object?>
        {
            ["newResults"] = newResults,
            ["resolved"] = resolved,
            ["persisting"] = persisting,
            ["counts"] = new Dictionary<string, object?>
            {
                ["new"] = newResults.Count,
                ["resolved"] = resolved.Count,
                ["persisting"] = persisting.Count,
                ["oldTotal"] = oldRoot.Results.Count,
                ["newTotal"] = newRoot.Results.Count,
            },
        };
    }

    /// <summary>
    /// A stable identity for one clashing item: its InstanceGuid when the
    /// source format provides one, otherwise its selection-tree path.
    /// </summary>
    private static string ItemIdentity(ModelItem? item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        var guid = item.InstanceGuid;
        return guid != Guid.Empty
            ? "guid:" + guid.ToString("N")
            : "path:" + NavisValues.ItemPath(item);
    }
}
