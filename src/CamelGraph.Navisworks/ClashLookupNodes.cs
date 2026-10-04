using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes;
using CamelGraph.Nodes.Coordination;

namespace CamelGraph.Navisworks;

/// <summary>
/// Nodes that turn clash results into data and back again: a table of (filtered) results, a result found again from the GUID a
/// BCF topic carries, and the live results that a saved snapshot did or did not have (the "new this week" list).
/// </summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashLookupNodes
{
    /// <summary>A table with one row per clash result.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results or any Clash.FilterBy* node).</param>
    /// <param name="units">Unit of the distance and the clash point: "document" uses the file's internal unit (often feet!), or name the unit you want the numbers in.</param>
    /// <returns>A table with the columns Test, Group, Result, Result GUID, Status, Distance, Assigned To, Description, Created, Item 1, Item 1 GUID, Item 2, Item 2 GUID, Center X, Center Y and Center Z.</returns>
    [NodeName("Clash.ResultsTable")]
    [NodeCategory("Navisworks.Clash.Report")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Makes a table of the clash results you wire in: one row per result with its test, group, name, GUID, status, " +
        "distance, assignee, description, creation date, the two items (path and GUID) and the clash point. Wire any " +
        "filter's output, so \"the New clashes deeper than 25 mm\" is one row each, ready for the Table nodes, Table.ToCsvFile, " +
        "Excel.WriteTable or Report.Html. Distance and clash point are in document units unless units names another " +
        "unit; the creation date is a real date. An empty list gives a table with the headers and no rows.")]
    [NodeSearchTags("clash", "results", "table", "report", "export", "rows", "csv", "excel", "list", "filtered")]
    [return: NodeName("table")]
    public static CamelGraphTable ResultsTable(
        [MultiInput] IEnumerable<ClashResult> results,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document")
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var scale = string.IsNullOrWhiteSpace(units) || units.Trim().Equals("document", StringComparison.OrdinalIgnoreCase)
            ? 1.0
            : NavisValues.ResolveUnitsScale(NavisworksContext.ResolveDocument(null), units);

        var rows = new List<IReadOnlyList<object?>>();
        try
        {
            foreach (var result in results)
            {
                if (result == null)
                {
                    continue;
                }

                ClashHelpers.OwnerNames(result, out var testName, out var groupName);
                var center = result.Center;
                var item1 = result.Item1;
                var item2 = result.Item2;
                rows.Add(new object?[]
                {
                    testName,
                    groupName,
                    result.DisplayName ?? string.Empty,
                    GuidText(result.Guid),
                    result.Status.ToString(),
                    result.Distance / scale,
                    ClashHelpers.AssigneeText(result),
                    result.Description ?? string.Empty,
                    result.CreatedTime,
                    NavisValues.ItemPath(item1),
                    GuidText(item1?.InstanceGuid ?? Guid.Empty),
                    NavisValues.ItemPath(item2),
                    GuidText(item2?.InstanceGuid ?? Guid.Empty),
                    center == null ? (object?)null : center.X / scale,
                    center == null ? (object?)null : center.Y / scale,
                    center == null ? (object?)null : center.Z / scale,
                });
            }
        }
        catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
        {
            throw ClashHelpers.StaleInputError("clash results", ex);
        }

        if (rows.Count == 0)
        {
            NodeWarnings.Add("No clash results were given, so the table has no rows.");
        }

        return new CamelGraphTable(ClashResultColumns.Headers, rows);
    }

    /// <summary>Finds clash results (or result groups) again from their GUIDs.</summary>
    /// <param name="guids">The GUIDs as text (e.g. the guid output of BCF.ImportIssues or of ClashResult.Info): one, or a list.</param>
    /// <param name="tests">The tests to look in. Leave unwired for every test in the document; a wired list means exactly those tests.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The clash results or groups that were found, in the order of the GUIDs, and the GUIDs that were not found.</returns>
    [NodeName("ClashResult.ByGuid")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [LiveState]
    [NodeDescription(
        "Finds clash results again from their GUIDs — the way back from a BCF topic or a snapshot to the live result: wire the " +
        "guid output of BCF.ImportIssues and send the results to ClashResult.SetStatus. A GUID that belongs to a result group " +
        "finds the group (the BCF export writes one topic per result or group). GUIDs may be written with or without braces, " +
        "in any case. results holds what was found, in the order of the GUIDs; missing holds the GUIDs that matched nothing " +
        "(not a GUID, or a result that is no longer in the tests you looked in). Leave tests unwired to search every test " +
        "in the document; a wired list means exactly those tests.")]
    [NodeSearchTags("clash", "result", "guid", "find", "lookup", "bcf", "topic", "id", "byguid")]
    [MultiReturn("results", "missing")]
    [PortKinds("clash*", "text*")]
    public static Dictionary<string, object?> ByGuid(
        IEnumerable<string> guids,
        [MultiInput] IEnumerable<ClashTest>? tests = null,
        Document? document = null)
    {
        if (guids == null)
        {
            throw new ArgumentNullException(nameof(guids), "No GUIDs provided.");
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var testList = ClashInputs.SelectedOrAll<ClashTest>(
            tests, () => NavisValues.FlattenSavedItems<ClashTest>(clash.TestsData.Tests));

        var candidates = new List<KeyValuePair<Guid, SavedItem>>();
        foreach (var test in testList)
        {
            ClashHelpers.CollectResultsAndGroups(test.Children, candidates);
        }

        ClashGuids.Match(guids, candidates, out var found, out var missing);
        if (missing.Count > 0)
        {
            NodeWarnings.Add(
                missing.Count + " of " + (found.Count + missing.Count) + " GUID(s) matched no clash result or group" +
                (testList.Count == 0 ? " (no clash test was given to look in)." : "."));
        }

        return new Dictionary<string, object?>
        {
            ["results"] = found,
            ["missing"] = missing,
        };
    }

    /// <summary>Keeps the live clash results that a saved snapshot did not have (new) or already had (persisting).</summary>
    /// <param name="results">The live clash results (e.g. from ClashTest.Results).</param>
    /// <param name="snapshotPath">The snapshot .json written by Clash.SnapshotToFile (last week's file).</param>
    /// <param name="keep">new keeps the results that are not in the snapshot; persisting keeps the ones that are.</param>
    /// <returns>The results that were kept, and the others, both in input order.</returns>
    [NodeName("Clash.FilterBySnapshot")]
    [NodeCategory("Navisworks.Clash.Filter")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "Turns \"what is new since last week\" into live clash results you can select, group, assign or report: compares the " +
        "results with a snapshot saved by Clash.SnapshotToFile, using the same identity as Clash.CompareSnapshots (the test and " +
        "the pair of items, by InstanceGuid or tree path), and keeps the new ones (not in the snapshot) or the persisting ones " +
        "(already in it). The other group comes out on others. Clashes that were resolved no longer exist in the model, so " +
        "they can only be listed by Clash.CompareSnapshots. The same two items clashing at several points are matched one for " +
        "one, in order.")]
    [NodeSearchTags("clash", "filter", "snapshot", "new", "persisting", "delta", "baseline", "weekly", "compare", "history")]
    [MultiReturn("results", "others")]
    [PortKinds("clash*", "clash*")]
    public static Dictionary<string, object?> FilterBySnapshot(
        [MultiInput] IEnumerable<ClashResult> results,
        [NodePath(NodePathMode.Open, Filter = "Clash snapshots (*.json)|*.json")] string snapshotPath,
        [NodeChoices("new", "persisting")] string keep = "new")
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var wantPersisting = ParseKeep(keep);
        var snapshot = ClashSnapshotFile.Read(PathResolver.Resolve(snapshotPath));

        var live = new List<ClashResult>();
        var keys = new List<string>();
        try
        {
            foreach (var result in results)
            {
                if (result == null)
                {
                    continue;
                }

                ClashHelpers.OwnerNames(result, out var testName, out _);
                live.Add(result);
                keys.Add(ClashSnapshotFile.MakeKey(
                    testName, NavisValues.ItemIdentity(result.Item1), NavisValues.ItemIdentity(result.Item2)));
            }
        }
        catch (Exception ex) when (ClashHelpers.IsDisposed(ex))
        {
            throw ClashHelpers.StaleInputError("clash results", ex);
        }

        var snapshotKeys = new List<string>(snapshot.Results.Count);
        foreach (var entry in snapshot.Results)
        {
            snapshotKeys.Add(entry.Key);
        }

        var inSnapshot = ClashSnapshotMatch.WasInSnapshot(snapshotKeys, keys);
        var kept = new List<ClashResult>();
        var others = new List<ClashResult>();
        for (var i = 0; i < live.Count; i++)
        {
            (inSnapshot[i] == wantPersisting ? kept : others).Add(live[i]);
        }

        return new Dictionary<string, object?>
        {
            ["results"] = kept,
            ["others"] = others,
        };
    }

    private static bool ParseKeep(string? keep)
    {
        switch ((keep ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "":
            case "new": return false;
            case "persisting": return true;
            default:
                throw new ArgumentException("Unknown keep '" + keep + "'. Use new or persisting.", nameof(keep));
        }
    }

    private static string GuidText(Guid guid) => guid == Guid.Empty ? string.Empty : guid.ToString();
}
