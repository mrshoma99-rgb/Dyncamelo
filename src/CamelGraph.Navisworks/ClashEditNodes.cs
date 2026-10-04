using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;

namespace CamelGraph.Navisworks;

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
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeDescription("Renames a clash test — wire a test or its current name. Batch-rename the whole matrix via lacing: a list of tests (or of names) and a list of new names pair up one to one, one test per name.")]
    [NodeSearchTags("clash", "test", "rename", "name")]
    [return: NodeName("test")]
    public static ClashTest Rename([ScalarInput] object test, string newName, Document? document = null)
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
    [NodeCategory("Navisworks.Clash.Results")]
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
    [NodeDeprecated("SavedItem.AddComment")]
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
        return SavedItemCommentNodes.AddComment(result, body, status, author, document);
    }

    /// <summary>Writes a comment onto a clash result or result group (the clash half of SavedItem.AddComment).</summary>
    internal static SavedItem AddClashComment(SavedItem result, string body, string status, string author, Document? document)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result), "No clash result provided.");
        }

        var clashResult = result as IClashResult
            ?? throw new ArgumentException("'" + result.DisplayName + "' is not a clash result or result group.", nameof(result));
        if (string.IsNullOrEmpty(body))
        {
            throw new ArgumentException("No comment body provided.", nameof(body));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var comment = string.IsNullOrEmpty(author)
            ? doc.CreateCommentWithUniqueId(body, NavisValues.ParseCommentStatus(status))
            : doc.CreateCommentWithUniqueId(body, NavisValues.ParseCommentStatus(status), author);

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
    [NodeDeprecated("SavedItem.Comments")]
    [NodeDescription("The comment thread of a clash result or group: texts, authors, statuses and dates, index-aligned — feeds reports and BCF export.")]
    [NodeSearchTags("clash", "result", "comments", "read", "thread", "review")]
    [MultiReturn("comments", "authors", "statuses", "dates")]
    [PortKinds("text*", "text*", "text*", "datetime*")]
    public static Dictionary<string, object?> Comments(SavedItem result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result), "No clash result provided.");
        }

        var thread = SavedItemCommentNodes.Comments(result);
        return new Dictionary<string, object?>
        {
            ["comments"] = thread["bodies"],
            ["authors"] = thread["authors"],
            ["statuses"] = thread["statuses"],
            ["dates"] = thread["dates"],
        };
    }

    /// <summary>Groups a test's results by their status.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByStatus")]
    [NodeCategory("Navisworks.Clash.Group")]
    [NodeDescription("Groups a test's results by status (New/Active/Reviewed/Approved/Resolved) — one triage bucket per status in Clash Detective.")]
    [NodeSearchTags("clash", "group", "status", "triage", "bucket")]
    [MultiReturn("test", "groupCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> GroupResultsByStatus(ClashTest test, Document? document = null)
    {
        return ClashRegroup.Commit(test, document, results =>
            Partition(results, r => r.Status.ToString()));
    }

    /// <summary>Groups a test's results by the nearest grid intersection.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByGridIntersection")]
    [NodeCategory("Navisworks.Clash.Group")]
    [NodeDescription("Groups a test's results by the model's own grid: each group is named after the nearest grid intersection and level (e.g. \"B-3 : Level 2\"). Requires a document with grids (Revit/IFC sources).")]
    [NodeSearchTags("clash", "group", "grid", "intersection", "level", "location", "triage")]
    [MultiReturn("test", "groupCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> GroupResultsByGridIntersection(
        ClashTest test,
        Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var system = doc.Grids?.ActiveSystem
            ?? throw new InvalidOperationException(
                "The document has no active grid system. Grids come from source models " +
                "(e.g. Revit or IFC files with grids and levels) — for hand-typed levels use Clash.GroupResultsByLevel.");

        return ClashRegroup.Commit(test, document, results =>
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
    [NodeCategory("Navisworks.Clash.Report")]
    [NodeDescription("Per-test clash counts by status (test × Total/New/Active/Reviewed/Approved/Resolved) — the clash summary matrix, ready for CSV.WriteToFile or Excel.WriteToFile.")]
    [NodeSearchTags("clash", "summary", "table", "matrix", "counts", "report", "excel")]
    [MultiReturn("rows", "headers")]
    [PortKinds("", "text*")]
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
        return ClashHelpers.ResolveStoredTest(clash, test);
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
}
