using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>
/// Clash management nodes added in v0.3 (WS-E): renaming tests and results,
/// result comments, grouping by status / grid intersection and the per-test
/// summary table. Read nodes live in <see cref="ClashNodes"/>.
/// </summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashEditNodes
{
    /// <summary>Renames a clash test.</summary>
    /// <param name="test">The clash test, or its current display name.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored test (pass-through for chaining).</returns>
    [NodeName("ClashTest.Rename")]
    [NodeDescription("Renames a clash test — wire a test or its current name. Batch-rename the whole matrix via lacing.")]
    [NodeSearchTags("clash", "test", "rename", "name")]
    [return: NodeName("test")]
    public static ClashTest Rename(object test, string newName, Document? document = null)
    {
        RequireName(newName);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var stored = ResolveStoredTest(clash, test);
        clash.TestsData.TestsEditDisplayName(stored, newName);
        return stored;
    }

    /// <summary>Renames a clash result or result group.</summary>
    /// <param name="result">The clash result (from ClashTest.Results) or group.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The result (pass-through). Lace over results + String nodes for Smart-Results-style batch naming (e.g. "Pipe vs Duct L02-B3").</returns>
    [NodeName("ClashResult.Rename")]
    [NodeDescription("Renames a clash result or result group — with lacing and String nodes this is batch renaming (\"Clash1\" → \"Pipe vs Duct L02-B3\").")]
    [NodeSearchTags("clash", "result", "rename", "name", "smart", "batch")]
    [return: NodeName("result")]
    public static SavedItem RenameResult(SavedItem result, string newName, Document? document = null)
    {
        RequireName(newName);
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result), "No clash result provided.");
        }

        if (!(result is IClashResult))
        {
            throw new ArgumentException(
                "'" + result.DisplayName + "' is not a clash result or result group. " +
                "Wire a result from ClashTest.Results (for tests use ClashTest.Rename).", nameof(result));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        clash.TestsData.TestsEditDisplayName(result, newName);
        return result;
    }

    /// <summary>Appends a comment to a clash result or result group.</summary>
    /// <param name="result">The clash result (from ClashTest.Results) or group.</param>
    /// <param name="body">The comment text.</param>
    /// <param name="status">"New", "Active", "Approved" or "Resolved".</param>
    /// <param name="author">Comment author ("" uses the Navisworks user name).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The result (pass-through). Lace over result lists for review notes in bulk.</returns>
    [NodeName("ClashResult.AddComment")]
    [NodeDescription("Appends a comment to a clash result or group — review notes in bulk, and the sync-back half of BCF round trips.")]
    [NodeSearchTags("clash", "result", "comment", "add", "note", "review", "bcf")]
    [return: NodeName("result")]
    public static SavedItem AddComment(
        SavedItem result,
        string body,
        [NodeChoices("New", "Active", "Approved", "Resolved")]
        string status = "New",
        string author = "",
        Document? document = null)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result), "No clash result provided.");
        }

        var clashResult = result as IClashResult
            ?? throw new ArgumentException(
                "'" + result.DisplayName + "' is not a clash result or result group. " +
                "For viewpoints and sets use SavedItem.AddComment.", nameof(result));
        if (string.IsNullOrEmpty(body))
        {
            throw new ArgumentException("No comment body provided.", nameof(body));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var comment = string.IsNullOrEmpty(author)
            ? doc.CreateCommentWithUniqueId(body, ParseCommentStatus(status))
            : doc.CreateCommentWithUniqueId(body, ParseCommentStatus(status), author);

        // Stored comments are read-only: copy the thread, append, write back.
        var comments = new CommentCollection(result.Comments);
        comments.Add(comment);
        clash.TestsData.TestsEditResultComments(clashResult, comments);
        return result;
    }

    /// <summary>Reads the comment thread of a clash result or group.</summary>
    /// <param name="result">The clash result or result group.</param>
    /// <returns>Index-aligned comment texts, authors, statuses and creation dates.</returns>
    [NodeName("ClashResult.Comments")]
    [NodeDescription("The comment thread of a clash result or group: texts, authors, statuses and dates, index-aligned — feeds reports and BCF export.")]
    [NodeSearchTags("clash", "result", "comments", "read", "thread", "review")]
    [MultiReturn("comments", "authors", "statuses", "dates")]
    public static Dictionary<string, object?> Comments(SavedItem result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result), "No clash result provided.");
        }

        var comments = new List<string>();
        var authors = new List<string>();
        var statuses = new List<string>();
        var dates = new List<DateTime>();
        foreach (var comment in result.Comments)
        {
            comments.Add(comment.Body ?? string.Empty);
            authors.Add(comment.Author ?? string.Empty);
            statuses.Add(comment.Status.ToString());
            dates.Add(comment.CreationDate);
        }

        return new Dictionary<string, object?>
        {
            ["comments"] = comments,
            ["authors"] = authors,
            ["statuses"] = statuses,
            ["dates"] = dates,
        };
    }

    /// <summary>Groups a test's results by their status.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByStatus")]
    [NodeDescription("Groups a test's results by status (New/Active/Reviewed/Approved/Resolved) — one triage bucket per status in Clash Detective.")]
    [NodeSearchTags("clash", "group", "status", "triage", "bucket")]
    [MultiReturn("test", "groupCount")]
    public static Dictionary<string, object?> GroupResultsByStatus(ClashTest test, Document? document = null)
    {
        return RegroupAndCommit(test, document, results =>
            Partition(results, r => r.Status.ToString()));
    }

    /// <summary>Groups a test's results by the nearest grid intersection.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByGridIntersection")]
    [NodeDescription("Groups a test's results by the model's own grid: each group is named after the nearest grid intersection and level (e.g. \"B-3 : Level 2\"). Requires a document with grids (Revit/IFC sources).")]
    [NodeSearchTags("clash", "group", "grid", "intersection", "level", "location", "triage")]
    [MultiReturn("test", "groupCount")]
    public static Dictionary<string, object?> GroupResultsByGridIntersection(
        ClashTest test,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var system = doc.Grids?.ActiveSystem
            ?? throw new InvalidOperationException(
                "The document has no active grid system. Grids come from source models " +
                "(e.g. Revit or IFC files with grids and levels) — for hand-typed levels use Clash.GroupResultsByLevel.");

        return RegroupAndCommit(test, document, results =>
            Partition(results, r =>
            {
                var center = r.Center;
                var intersection = system.ClosestIntersection(center);
                return intersection == null
                    ? "(no grid intersection)"
                    : intersection.FormatCombinedDisplayString(center, 1.0);
            }));
    }

    /// <summary>Per-test result counts by status.</summary>
    /// <param name="tests">The tests to summarize (empty/unwired = every test in the document).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Rows (one per test) and headers — wire straight into CSV.WriteToFile or Excel.WriteToFile.</returns>
    [NodeName("Clash.SummaryTable")]
    [NodeDescription("Per-test clash counts by status (test × Total/New/Active/Reviewed/Approved/Resolved) — the clash summary matrix, ready for CSV.WriteToFile or Excel.WriteToFile.")]
    [NodeSearchTags("clash", "summary", "table", "matrix", "counts", "report", "excel")]
    [MultiReturn("rows", "headers")]
    public static Dictionary<string, object?> SummaryTable(
        IEnumerable<ClashTest>? tests = null,
        Document? document = null)
    {
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

        var statusNames = Enum.GetNames(typeof(ClashResultStatus));
        var headers = new List<string> { "Test", "Total" };
        headers.AddRange(statusNames);

        var rows = new List<List<object?>>();
        foreach (var test in testList)
        {
            var results = ClashHelpers.FlattenResults(test);
            var countsByStatus = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var result in results)
            {
                var status = result.Status.ToString();
                countsByStatus.TryGetValue(status, out var count);
                countsByStatus[status] = count + 1;
            }

            var row = new List<object?> { test.DisplayName, results.Count };
            foreach (var statusName in statusNames)
            {
                countsByStatus.TryGetValue(statusName, out var count);
                row.Add(count);
            }

            rows.Add(row);
        }

        return new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["headers"] = headers,
        };
    }

    // ------------------------------------------------------------ privates

    private static void RequireName(string newName)
    {
        if (string.IsNullOrEmpty(newName))
        {
            throw new ArgumentException("No new name provided.", nameof(newName));
        }
    }

    /// <summary>Resolves a "test or name" input to the STORED clash test.</summary>
    private static ClashTest ResolveStoredTest(DocumentClash clash, object? test)
    {
        switch (test)
        {
            case null:
                throw new ArgumentNullException(nameof(test), "No clash test provided.");
            case string name:
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("No clash test name provided.", nameof(test));
                }

                return NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name)
                    ?? throw new InvalidOperationException(
                        "No clash test named '" + name + "' exists in the document.");
            case ClashTest clashTest:
                if (clashTest.IsReadOnly)
                {
                    return clashTest; // already the stored instance
                }

                // A detached copy — re-locate the stored original by Guid/name.
                var byGuid = clashTest.Guid != Guid.Empty
                    ? FindTestByGuid(clash.TestsData.Tests, clashTest.Guid)
                    : null;
                return byGuid
                    ?? NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, clashTest.DisplayName)
                    ?? throw new InvalidOperationException(
                        "The clash test '" + clashTest.DisplayName + "' is not stored in this document. " +
                        "Wire a test from Clash.Tests, ClashTest.ByName or ClashTest.Create.");
            default:
                throw new ArgumentException(
                    "Cannot interpret a value of type '" + test.GetType().Name +
                    "' as a clash test. Wire the test itself or its display name.", nameof(test));
        }
    }

    private static ClashTest? FindTestByGuid(IEnumerable<SavedItem> items, Guid guid)
    {
        foreach (var test in NavisValues.FlattenSavedItems<ClashTest>(items))
        {
            if (test.Guid == guid)
            {
                return test;
            }
        }

        return null;
    }

    private static CommentStatus ParseCommentStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return CommentStatus.New;
        }

        switch (status!.Trim().ToLowerInvariant())
        {
            case "new": return CommentStatus.New;
            case "active": return CommentStatus.Active;
            case "approved": return CommentStatus.Approved;
            case "resolved": return CommentStatus.Resolved;
            default:
                throw new ArgumentException(
                    "Unknown comment status '" + status + "'. Use \"New\", \"Active\", \"Approved\" or \"Resolved\".",
                    nameof(status));
        }
    }

    /// <summary>Buckets results by a name key, keeping first-seen bucket order.</summary>
    private static List<KeyValuePair<string, List<ClashResult>>> Partition(
        List<ClashResult> results,
        Func<ClashResult, string> keyOf)
    {
        var buckets = new List<KeyValuePair<string, List<ClashResult>>>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var key = keyOf(result);
            if (string.IsNullOrEmpty(key))
            {
                key = "(none)";
            }

            if (!indexByKey.TryGetValue(key, out var bucketIndex))
            {
                bucketIndex = buckets.Count;
                indexByKey[key] = bucketIndex;
                buckets.Add(new KeyValuePair<string, List<ClashResult>>(key, new List<ClashResult>()));
            }

            buckets[bucketIndex].Value.Add(result);
        }

        return buckets;
    }

    /// <summary>
    /// Rebuilds a stored test's result tree from a partition and commits it in
    /// one <c>TestsEditTestFromCopy</c> edit (same commit path as the v0.2
    /// grouping nodes): buckets of two or more become named
    /// <see cref="ClashResultGroup"/>s, singletons stay ungrouped.
    /// </summary>
    private static Dictionary<string, object?> RegroupAndCommit(
        ClashTest test,
        Document? document,
        Func<List<ClashResult>, List<KeyValuePair<string, List<ClashResult>>>> partition)
    {
        var stored = ClashHelpers.RequireStoredTest(test);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var copy = (ClashTest)stored.CreateCopy();
        var flattened = ClashHelpers.FlattenResults(copy);

        // Detach every result from the copy's tree before rebuilding it —
        // Children.Clear() below destroys the originals.
        var detached = new List<ClashResult>(flattened.Count);
        foreach (var result in flattened)
        {
            detached.Add((ClashResult)result.CreateCopy());
        }

        var clusters = partition(detached);

        copy.Children.Clear();
        var groupCount = 0;
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var singles = new List<ClashResult>();
        foreach (var cluster in clusters)
        {
            if (cluster.Value.Count < 2)
            {
                singles.AddRange(cluster.Value);
                continue;
            }

            var name = cluster.Key;
            var suffix = 2;
            while (!usedNames.Add(name))
            {
                name = cluster.Key + " (" + suffix++ + ")";
            }

            var group = new ClashResultGroup { DisplayName = name };
            foreach (var result in cluster.Value)
            {
                group.Children.Add(result);
            }

            copy.Children.Add(group);
            groupCount++;
        }

        foreach (var result in singles)
        {
            copy.Children.Add(result);
        }

        clash.TestsData.TestsEditTestFromCopy(stored, copy);
        return new Dictionary<string, object?>
        {
            ["test"] = stored,
            ["groupCount"] = groupCount,
        };
    }
}
