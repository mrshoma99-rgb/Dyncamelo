using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelGraph.Core.Execution;
using CamelGraph.Core.Files;
using CamelGraph.Core.Loader;
using CamelGraph.Navisworks.Internal;
using CamelGraph.Nodes.Coordination;
using CamelGraph.Nodes.Spatial;

namespace CamelGraph.Navisworks;

/// <summary>Nodes for reading Clash Detective tests and results.</summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashNodes
{
    /// <summary>All clash tests in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every clash test, including those nested in folders.</returns>
    [NodeName("Clash.Tests")]
    [LiveState]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeDescription("All Clash Detective tests in a document, including those inside folders.")]
    [NodeSearchTags("clash", "tests", "detective", "all")]
    [return: NodeName("tests")]
    public static List<ClashTest> Tests(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = doc.GetClash()
            ?? throw new InvalidOperationException("Clash Detective is not available in this Navisworks edition.");
        return NavisValues.FlattenSavedItems<ClashTest>(clash.TestsData.Tests);
    }

    /// <summary>Summary information about a clash test.</summary>
    /// <param name="test">The clash test.</param>
    /// <param name="units">Unit to give the tolerance in: "document" uses the file's internal unit (often feet!), or name a unit.</param>
    /// <returns>Name, status, type, tolerance, last-run time and result count.</returns>
    [NodeName("ClashTest.Info")]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.Info@Autodesk.Navisworks.Api.Clash.ClashTest")]
    [NodeDescription("Name, status, type, tolerance, last run time and result count of a clash test. The tolerance is in document units unless units names another unit.")]
    [NodeSearchTags("clash", "test", "info", "status", "tolerance")]
    [MultiReturn("name", "status", "testType", "tolerance", "lastRun", "resultCount")]
    [PortKinds("text", "text", "text", "number", "datetime", "integer")]
    public static Dictionary<string, object?> Info(
        ClashTest test,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document")
    {
        var clashTest = ClashHelpers.RequireTest(test);
        // The document is only needed to convert; the default (document units) reads the test as it is.
        var scale = string.IsNullOrWhiteSpace(units) || units.Trim().Equals("document", StringComparison.OrdinalIgnoreCase)
            ? 1.0
            : NavisValues.ResolveUnitsScale(NavisworksContext.ResolveDocument(null), units);
        return new Dictionary<string, object?>
        {
            ["name"] = clashTest.DisplayName,
            ["status"] = clashTest.Status.ToString(),
            ["testType"] = clashTest.TestType.ToString(),
            ["tolerance"] = clashTest.Tolerance / scale,
            ["lastRun"] = clashTest.LastRun,
            ["resultCount"] = FlattenResults(clashTest).Count,
        };
    }

    /// <summary>The individual clash results of a test.</summary>
    /// <param name="test">The clash test.</param>
    /// <returns>Every result, with grouped results flattened.</returns>
    [NodeName("ClashTest.Results")]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeDescription("The individual results of a clash test (grouped results are flattened).")]
    [NodeSearchTags("clash", "test", "results", "clashes")]
    [return: NodeName("results")]
    public static List<ClashResult> Results(ClashTest test)
    {
        return FlattenResults(ClashHelpers.RequireTest(test));
    }

    /// <summary>Summary information about a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>Name, status, distance, description, assignee, creation time, GUID, the name of its test and of its group.</returns>
    [NodeName("ClashResult.Info")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeDescription("Name, status, distance, description, assignee and creation time of a clash result, plus its GUID (what BCF topics and ClashResult.ByGuid use), the name of its test and the name of its group (empty when it is not in a group).")]
    [NodeSearchTags("clash", "result", "info", "status", "distance")]
    [MultiReturn("name", "status", "distance", "description", "assignedTo", "createdTime", "guid", "testName", "group")]
    [PortKinds("text", "text", "number", "text", "text", "datetime", "text", "text", "text")]
    public static Dictionary<string, object?> ResultInfo(ClashResult result)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        ClashHelpers.OwnerNames(clashResult, out var testName, out var groupName);
        return new Dictionary<string, object?>
        {
            ["name"] = clashResult.DisplayName,
            ["status"] = clashResult.Status.ToString(),
            ["distance"] = clashResult.Distance,
            ["description"] = clashResult.Description,
            ["assignedTo"] = ClashHelpers.AssigneeText(clashResult),
            ["createdTime"] = clashResult.CreatedTime,
            ["guid"] = clashResult.Guid == Guid.Empty ? string.Empty : clashResult.Guid.ToString(),
            ["testName"] = testName,
            ["group"] = groupName,
        };
    }

    /// <summary>The two model items involved in a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The clashing items.</returns>
    [NodeName("ClashResult.Items")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeDescription("The two model items involved in a clash result.")]
    [NodeSearchTags("clash", "result", "items", "elements", "pair")]
    [MultiReturn("item1", "item2")]
    [PortKinds("item", "item")]
    public static Dictionary<string, object?> ResultItems(ClashResult result)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        return new Dictionary<string, object?>
        {
            ["item1"] = clashResult.Item1,
            ["item2"] = clashResult.Item2,
        };
    }

    /// <summary>The clash point of a result.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The clash center point, in document units.</returns>
    [NodeName("ClashResult.Center")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeDescription("The clash point of a result, in document units.")]
    [NodeSearchTags("clash", "result", "center", "point", "location")]
    [return: NodeName("point")]
    public static Point3D Center(ClashResult result)
    {
        return ClashHelpers.RequireResult(result).Center;
    }

    /// <summary>The crossing angle between the two clashing elements.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The angle in degrees (0–90) between the two elements' overall directions.</returns>
    [NodeName("ClashResult.Angle")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "The angle in degrees (0–90) between the two clashing elements, taken from each element's overall " +
        "direction (its bounding-box diagonal). ~0° means they run parallel/together, ~90° means they cross " +
        "perpendicular — so you can filter clashes by crossing angle (e.g. keep only ~90° crossings). Returns " +
        "NaN when an element has no measurable direction (a point-like box).")]
    [NodeSearchTags("clash", "angle", "degrees", "perpendicular", "crossing", "orientation", "direction")]
    [return: NodeName("degrees")]
    public static double Angle(ClashResult result)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        if (!TryElementDirection(clashResult.Item1, out var d1) ||
            !TryElementDirection(clashResult.Item2, out var d2))
        {
            return double.NaN;
        }

        // Elements have no head/tail, so treat the direction as an undirected axis:
        // |dot| collapses parallel/anti-parallel to 0° and perpendicular to 90°.
        var dot = Math.Abs((d1.X * d2.X) + (d1.Y * d2.Y) + (d1.Z * d2.Z));
        dot = Math.Max(0.0, Math.Min(1.0, dot));
        return Math.Acos(dot) * (180.0 / Math.PI);
    }

    /// <summary>The size of the clash overlap region (its bounding-box volume).</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The clash bounding-box volume in document units³ (0 when unavailable).</returns>
    [NodeName("ClashResult.Size")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("The size of the clash overlap region — its bounding-box volume in document units³. Filter out tiny grazing clashes and keep the significant ones. Pair with Units.Convert for readable units.")]
    [NodeSearchTags("clash", "size", "volume", "extent", "significance", "big", "small", "filter")]
    [return: NodeName("volume")]
    public static double Size(ClashResult result)
    {
        var box = ClashHelpers.RequireResult(result).BoundingBox;
        if (box == null)
        {
            return 0.0;
        }

        return Math.Abs(box.Max.X - box.Min.X) * Math.Abs(box.Max.Y - box.Min.Y) * Math.Abs(box.Max.Z - box.Min.Z);
    }

    /// <summary>How documented a clash result already is.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>Whether it has a saved viewpoint, redline markup, and its comment count.</returns>
    [NodeName("ClashResult.Documentation")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("How documented a clash already is — whether it has a saved viewpoint, redline markup, and how many comments. Filter to the reviewed/annotated ones, or find the ones still needing attention (commentCount = 0).")]
    [NodeSearchTags("clash", "comments", "viewpoint", "redline", "reviewed", "documented", "annotated", "filter")]
    [MultiReturn("hasViewpoint", "hasRedlines", "commentCount")]
    [PortKinds("boolean", "boolean", "integer")]
    public static Dictionary<string, object?> Documentation(ClashResult result)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        return new Dictionary<string, object?>
        {
            ["hasViewpoint"] = clashResult.HasSavedViewpoint,
            ["hasRedlines"] = clashResult.HasRedlines,
            ["commentCount"] = clashResult.Comments?.Count ?? 0,
        };
    }

    /// <summary>Keeps only the clash results with the given status(es).</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="status">One or several of: New, Active, Reviewed, Approved, Resolved — comma-separated for several ("New,Active"; Clash.Statuses builds this from toggles).</param>
    /// <returns>The matching results, input order preserved.</returns>
    [NodeName("Clash.FilterByStatus")]
    [NodeCategory("Navisworks.Clash.Filter")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Keeps only the clash results with the given status(es) — pick one from the dropdown, or wire several comma-separated (\"New,Active\"; the Clash.Statuses node builds that from toggles). The triage staple: drop the already-Approved ones and work the rest.")]
    [NodeSearchTags("clash", "filter", "status", "statuses", "new", "active", "approved", "resolved", "triage", "multiple")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByStatus(
        [MultiInput] IEnumerable<ClashResult> results,
        [NodeChoices("New", "Active", "Reviewed", "Approved", "Resolved")]
        string status)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var wanted = ClashHelpers.ParseResultStatuses(status);
        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result != null && wanted.Contains(result.Status))
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    /// <summary>Keeps only the clash results whose crossing angle falls in a range.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="minDegrees">Lowest crossing angle to keep (0 = parallel).</param>
    /// <param name="maxDegrees">Highest crossing angle to keep (90 = perpendicular).</param>
    /// <returns>The matching results, input order preserved (angle-less results are dropped).</returns>
    [NodeName("Clash.FilterByAngle")]
    [NodeCategory("Navisworks.Clash.Filter")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Keeps only the clash results whose crossing angle (see ClashResult.Angle) is within a degree range — e.g. 80–90 for near-perpendicular crossings, or 0–10 for parallel runs. The angle is always between 0 (parallel) and 90 (perpendicular), so the range stops at 90. Results with no measurable direction are dropped.")]
    [NodeSearchTags("clash", "filter", "angle", "perpendicular", "parallel", "crossing", "degrees")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByAngle(
        [MultiInput] IEnumerable<ClashResult> results,
        [NodeRange(0, 90, Unit = "°")] double minDegrees = 0.0,
        [NodeRange(0, 90, Unit = "°")] double maxDegrees = 90.0)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result == null)
            {
                continue;
            }

            var angle = Angle(result);
            if (!double.IsNaN(angle) && angle >= minDegrees && angle <= maxDegrees)
            {
                matched.Add(result);
            }
        }

        return matched;
    }

    private static ClashResultStatus ParseResultStatus(string status)
    {
        switch ((status ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "new": return ClashResultStatus.New;
            case "active": return ClashResultStatus.Active;
            case "reviewed": return ClashResultStatus.Reviewed;
            case "approved": return ClashResultStatus.Approved;
            case "resolved": return ClashResultStatus.Resolved;
            default:
                throw new ArgumentException(
                    "Unknown clash status '" + status + "'. Use New, Active, Reviewed, Approved or Resolved.",
                    nameof(status));
        }
    }

    // A unit vector along an item's overall run, from its bounding-box diagonal —
    // a good proxy for the long axis of a linear element (pipe, duct, beam), even
    // a diagonal one. False when the box is degenerate (no direction to measure).
    private static bool TryElementDirection(ModelItem item, out (double X, double Y, double Z) direction)
    {
        direction = (0.0, 0.0, 0.0);
        if (item == null)
        {
            return false;
        }

        var box = item.BoundingBox(false);
        if (box == null)
        {
            return false;
        }

        var dx = box.Max.X - box.Min.X;
        var dy = box.Max.Y - box.Min.Y;
        var dz = box.Max.Z - box.Min.Z;
        var length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length < 1e-9)
        {
            return false;
        }

        direction = (dx / length, dy / length, dz / length);
        return true;
    }

    /// <summary>Finds a clash test by display name.</summary>
    /// <param name="name">The test's display name.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored clash test.</returns>
    [NodeName("ClashTest.ByName")]
    [LiveState]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeDescription("Finds a clash test by its display name (searches folders too).")]
    [NodeSearchTags("clash", "test", "byname", "find")]
    [return: NodeName("test")]
    public static ClashTest ByName(string name, Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No clash test name provided.", nameof(name));
        }

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var test = NavisValues.FindSavedItemByName<ClashTest>(clash.TestsData.Tests, name);
        return test ?? throw new InvalidOperationException(
            "No clash test named '" + name + "' exists in the document.");
    }

    /// <summary>Creates a clash test between two item selections, or finds the one that is already there.</summary>
    /// <param name="name">Display name for the test.</param>
    /// <param name="itemsA">Selection A (e.g. from a search or selection set).</param>
    /// <param name="itemsB">Selection B.</param>
    /// <param name="testType">One of: Hard, HardConservative, Clearance, Duplicate, Custom.</param>
    /// <param name="tolerance">Tolerance in the unit chosen under "units" (document units by default); the clearance distance for clearance tests.</param>
    /// <param name="units">Unit of the tolerance: "document" uses the file's internal unit (often feet!), or name the unit your number is in.</param>
    /// <param name="ifExists">What to do when a test with this name already exists: reuse (keep it as it is, the default), update (keep its results and apply the new settings), replace (a new empty test, results are lost) or error.</param>
    /// <param name="folder">Name of an existing Clash Detective folder to put a new test in; empty puts it at the top level.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored clash test, ready for ClashTest.Run.</returns>
    [NodeName("ClashTest.Create")]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.Create@string,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,System.Collections.Generic.IEnumerable<Autodesk.Navisworks.Api.ModelItem>,string,double,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Creates a clash test between two item selections — script the weekly test matrix instead of clicking it. If a test " +
        "with that name already exists, ifExists decides: reuse (the default) hands back the existing test untouched, with all " +
        "its results, statuses, assignments, comments and groups, and says so; update keeps those results and applies the " +
        "type, tolerance and selections you wired (re-run the test afterwards); replace swaps in a brand-new empty test, so " +
        "everything triaged on the old one is lost; error stops. A new test goes at the top level, or into the Clash " +
        "Detective folder named under folder (the folder must exist). The tolerance is in document units " +
        "unless units names another unit.")]
    [NodeSearchTags("clash", "test", "create", "new", "setup", "matrix")]
    [return: NodeName("test")]
    public static ClashTest Create(
        string name,
        IEnumerable<ModelItem> itemsA,
        IEnumerable<ModelItem> itemsB,
        [NodeChoices("Hard", "HardConservative", "Clearance", "Duplicate", "Custom")]
        string testType = "Hard",
        [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 1)] double tolerance = 0.01,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document",
        [NodeChoices("reuse", "update", "replace", "error")] string ifExists = "reuse",
        string folder = "",
        Document? document = null)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("No clash test name provided.", nameof(name));
        }

        if (itemsA == null)
        {
            throw new ArgumentNullException(nameof(itemsA), "No items provided for selection A.");
        }

        if (itemsB == null)
        {
            throw new ArgumentNullException(nameof(itemsB), "No items provided for selection B.");
        }

        var mode = ClashCreateRules.ParseIfExists(ifExists);
        var type = ClashHelpers.ParseTestType(testType);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        var scaledTolerance = tolerance * NavisValues.ResolveUnitsScale(doc, units);
        var tests = clash.TestsData;

        var existing = ClashHelpers.FindTestByName(tests.Tests, name);
        if (existing != null)
        {
            switch (mode)
            {
                case ClashIfExists.Error:
                    throw new InvalidOperationException(ClashCreateRules.ExistsMessage(name));
                case ClashIfExists.Reuse:
                    NodeWarnings.Add(ClashCreateRules.ReusedMessage(name));
                    return existing;
                case ClashIfExists.Update:
                    return ClashHelpers.UpdateTestSettings(doc, clash, existing, type, scaledTolerance, itemsA, itemsB);
            }
        }

        FolderItem? target = null;
        if (!string.IsNullOrWhiteSpace(folder))
        {
            target = ClashHelpers.FindFolder(tests.Tests, folder.Trim())
                ?? throw new InvalidOperationException(
                    "There is no clash test folder named '" + folder.Trim() + "' in the document. Create the folder in " +
                    "Clash Detective first, or leave folder empty to put the test at the top level.");
        }

        var test = new ClashTest
        {
            DisplayName = name,
            TestType = type,
            Tolerance = scaledTolerance,
        };
        test.SelectionA.Selection.CopyFrom(NavisValues.ToItemCollection(itemsA));
        test.SelectionB.Selection.CopyFrom(NavisValues.ToItemCollection(itemsB));

        using (var transaction = doc.BeginTransaction("Create clash test"))
        {
            if (existing != null)
            {
                // ifExists = replace: the new test takes the old one's place (in its folder, if it sits in one).
                if (!ClashHelpers.TryLocateTest(clash, existing, out var holder, out var index))
                {
                    throw new InvalidOperationException(
                        "The clash test '" + name + "' disappeared from the document while it was being replaced.");
                }

                if (holder == null)
                {
                    tests.TestsReplaceWithCopy(index, test);
                }
                else
                {
                    tests.TestsReplaceWithCopy(holder, index, test);
                }
            }
            else if (target != null)
            {
                tests.TestsAddCopy(target, test);
            }
            else
            {
                tests.TestsAddCopy(test);
            }

            transaction.Commit();
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        return ClashHelpers.FindTestByName(tests.Tests, name) ?? test;
    }

    /// <summary>Runs one clash test now.</summary>
    /// <param name="test">The stored clash test (from Clash.Tests, ClashTest.ByName or ClashTest.Create).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The test (pass-through) and its result count after the run.</returns>
    [NodeName("ClashTest.Run")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeDescription("Runs one clash test now and reports the result count.")]
    [NodeSearchTags("clash", "test", "run", "execute", "detect")]
    [MultiReturn("test", "resultCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> Run(ClashTest test, Document? document = null)
    {
        var stored = ClashHelpers.RequireStoredTest(test);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        clash.TestsData.TestsRunTest(stored);
        return new Dictionary<string, object?>
        {
            ["test"] = stored,
            ["resultCount"] = ClashHelpers.FlattenResults(stored).Count,
        };
    }

    /// <summary>Runs every clash test in the document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>All tests after the run.</returns>
    [NodeName("Clash.RunAllTests")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeCategory("Navisworks.Clash.Tests")]
    [NodeDescription("Runs every Clash Detective test in the document — the weekly coordination re-run in one node.")]
    [NodeSearchTags("clash", "run", "all", "tests", "batch")]
    [return: NodeName("tests")]
    public static List<ClashTest> RunAllTests(Document? document = null)
    {
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);
        clash.TestsData.TestsRunAllTests();
        return NavisValues.FlattenSavedItems<ClashTest>(clash.TestsData.Tests);
    }

    /// <summary>Filters a test's results by status.</summary>
    /// <param name="test">The clash test.</param>
    /// <param name="status">One or several of: New, Active, Reviewed, Approved, Resolved — comma-separated for several ("New,Active"; Clash.Statuses builds this from toggles).</param>
    /// <returns>The results with any of those statuses (grouped results are flattened).</returns>
    [NodeName("ClashTest.ResultsByStatus")]
    [NodeDeprecated("ClashTest.Results followed by Clash.FilterByStatus")]
    [NodeDescription("The results of a test that have the given status(es) — one, or several comma-separated (\"New,Active\"; wire Clash.Statuses to pick with toggles).")]
    [NodeSearchTags("clash", "results", "status", "statuses", "filter", "triage", "multiple")]
    [return: NodeName("results")]
    public static List<ClashResult> ResultsByStatus(ClashTest test, [NodeChoices("New", "Active", "Reviewed", "Approved", "Resolved")] string status)
    {
        return FilterByStatus(Results(test), status);
    }

    /// <summary>Sets the status of a clash result or of a whole result group.</summary>
    /// <param name="result">The clash result or result group; or, with list level 2 on this input, a whole list of them.</param>
    /// <param name="status">One of: New, Active, Reviewed, Approved, Resolved.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>What was wired in (pass-through): the same result, group or list.</returns>
    [NodeName("ClashResult.SetStatus")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.SetStatus@Autodesk.Navisworks.Api.Clash.ClashResult,string,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Sets the status of a clash result, or of a whole result group (wire the group from ClashTest.Groups or " +
        "ClashGroup.ByName) — approve a group in one step. A list of results runs the node once per result (lacing), so a " +
        "list of results and a list of statuses pair up one to one, e.g. the topic statuses from BCF.ImportIssues; each " +
        "result is its own edit and its own undo step. For a long list (thousands of results) right-click the result socket, " +
        "choose List Levels and @L2: the node then gets the whole list and edits all of it in one step, one undo step. " +
        "Works on Navisworks 2024 and 2025; on 2026 the node reports that it is not supported yet.")]
    [NodeSearchTags("clash", "result", "status", "set", "resolve", "approve", "triage", "group", "bulk")]
    [return: NodeName("result")]
    public static object? SetStatus(
        [ScalarInput][PortKinds("clash")] object result,
        [NodeChoices("New", "Active", "Reviewed", "Approved", "Resolved")] string status,
        Document? document = null)
    {
#if !NAV2026
        var wanted = ClashHelpers.ParseResultStatus(status);
        return ClashHelpers.EditResults(
            result, document, "Set clash status",
            (clash, item) => clash.TestsData.TestsEditResultStatus(item, wanted));
#else
        // TestsEditResultStatus gained a required Assignee (current-user) argument in
        // Navisworks 2026; pending a port verified on that release.
        throw new System.NotSupportedException(
            "ClashResult.SetStatus is currently supported on Navisworks 2024 and 2025; " +
            "the 2026 clash-status API differs and this node is pending an update.");
#endif
    }

    /// <summary>Assigns a clash result or a whole result group to a person or trade.</summary>
    /// <param name="result">The clash result or result group; or, with list level 2 on this input, a whole list of them.</param>
    /// <param name="assignedTo">The assignee (e.g. "MEP", "j.smith").</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>What was wired in (pass-through): the same result, group or list.</returns>
    [NodeName("ClashResult.Assign")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.Assign@Autodesk.Navisworks.Api.Clash.ClashResult,string,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Assigns a clash result, or a whole result group, to a person or trade. A list of results runs the node once per " +
        "result (lacing), so a list of results and a list of assignees pair up one to one; each result is its own edit. " +
        "For a long list right-click the result socket, choose List Levels and @L2: the node then gets the whole list and " +
        "assigns all of it in one step, one undo step. Works on Navisworks 2024 and 2025; on 2026 the node reports that it " +
        "is not supported yet.")]
    [NodeSearchTags("clash", "result", "assign", "trade", "responsible", "group", "bulk")]
    [return: NodeName("result")]
    public static object? Assign(
        [ScalarInput][PortKinds("clash")] object result,
        string assignedTo,
        Document? document = null)
    {
#if !NAV2026
        var assignee = assignedTo ?? string.Empty;
        return ClashHelpers.EditResults(
            result, document, "Assign clash results",
            (clash, item) => clash.TestsData.TestsEditResultAssignedTo(item, assignee));
#else
        // TestsEditResultAssignedTo takes an Assignee (not a string) in Navisworks 2026;
        // pending a port verified on that release.
        throw new System.NotSupportedException(
            "ClashResult.Assign is currently supported on Navisworks 2024 and 2025; " +
            "the 2026 clash-assignee API differs and this node is pending an update.");
#endif
    }

    /// <summary>Sets the description of a clash result or of a whole result group.</summary>
    /// <param name="result">The clash result or result group; or, with list level 2 on this input, a whole list of them.</param>
    /// <param name="description">The description text.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>What was wired in (pass-through): the same result, group or list.</returns>
    [NodeName("ClashResult.SetDescription")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.SetDescription@Autodesk.Navisworks.Api.Clash.ClashResult,string,Autodesk.Navisworks.Api.Document")]
    [NodeDescription(
        "Sets the description text of a clash result, or of a whole result group (context for reports and reviews). A list " +
        "of results runs the node once per result (lacing), so a list of results and a list of texts pair up one to one. For a " +
        "long list right-click the result socket, choose List Levels and @L2: the node then gets the whole list and edits " +
        "all of it in one step, one undo step.")]
    [NodeSearchTags("clash", "result", "description", "set", "note", "group", "bulk")]
    [return: NodeName("result")]
    public static object? SetDescription(
        [ScalarInput][PortKinds("clash")] object result,
        string description,
        Document? document = null)
    {
        var text = description ?? string.Empty;
        return ClashHelpers.EditResults(
            result, document, "Set clash description",
            (clash, item) => clash.TestsData.TestsEditResultDescription(item, text));
    }

    /// <summary>The auto-generated camera viewpoint of a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="apply">True to also make it the current view.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The viewpoint aimed at the clash.</returns>
    [NodeName("ClashResult.Viewpoint")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeDescription("The camera viewpoint Navisworks generates for a clash result; optionally applies it to the current view.")]
    [NodeSearchTags("clash", "result", "viewpoint", "camera", "goto")]
    [return: NodeName("viewpoint")]
    public static Viewpoint ResultViewpoint(ClashResult result, bool apply = false, Document? document = null)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        var doc = NavisworksContext.ResolveDocument(document);
        var viewpoint = ClashHelpers.RequireClash(doc).TestsData.TestsViewpointForResult(clashResult);
        if (apply)
        {
            doc.CurrentViewpoint.CopyFrom(viewpoint);
        }

        return viewpoint;
    }

    /// <summary>Renders a clash result snapshot to an image file.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="filePath">Destination .png, .jpg or .bmp path; the directory is created when missing.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The written file path. Lace over result lists for a snapshot folder.</returns>
    [NodeName("ClashResult.SaveImage")]
    [NodeCategory("Navisworks.Clash.Results")]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.WritesFiles)]
    [NodeDescription("Renders a clash snapshot (scene plus clash highlight) to a .png/.jpg/.bmp file — the picture half of every clash report.")]
    [NodeSearchTags("clash", "result", "image", "snapshot", "screenshot", "report")]
    [return: NodeName("filePath")]
    public static string SaveImage(
        ClashResult result,
        [NodePath(NodePathMode.Save, Filter = "Pictures (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp")] string filePath,
        [NodeRange(16, 8192, SoftMin = 320, SoftMax = 3840, Unit = "px")] int width = 1280,
        [NodeRange(16, 8192, SoftMin = 320, SoftMax = 3840, Unit = "px")] int height = 720,
        Document? document = null)
    {
        var clashResult = ClashHelpers.RequireResult(result);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

        // A relative path means next to the graph; quotes pasted from Explorer are dropped.
        filePath = PathResolver.Resolve(filePath);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Image width and height must be positive.");
        }

        var format = ImageFormatForExtension(filePath);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        using (var bitmap = clash.TestsData.TestsImageForResult(
            clashResult, ImageGenerationStyle.ScenePlusOverlay, width, height))
        {
            bitmap.Save(filePath, format);
        }

        return filePath;
    }

    /// <summary>Groups a test's results by the clashing element.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="useItem1">True to group on each result's item 1, false for item 2.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsBySameItem")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeCategory("Navisworks.Clash.Group")]
    [NodeDescription("Groups a test's results so every clash involving the same element lands in one group (named after the element) — turns thousands of raw clashes into one issue per element. Rebuilds the test's result tree: every group the test already has (also one made by Clash.GroupResults) is dissolved first and a group's own status, assignee and comments are not kept; a bucket with only one result stays ungrouped (a group needs two or more), so a status held by a single result gets no group. Both are reported as warnings.")]
    [NodeSearchTags("clash", "group", "same", "item", "element", "triage")]
    [MultiReturn("test", "groupCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> GroupResultsBySameItem(
        ClashTest test,
        bool useItem1 = true,
        Document? document = null)
    {
        return ClashRegroup.Commit(test, document, results => PartitionBySameItem(results, useItem1));
    }

    /// <summary>Groups a test's results into clusters of nearby clash points.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="radius">Cluster radius, in the unit chosen under "units" (document units by default).</param>
    /// <param name="units">Unit of the radius: "document" uses the file's internal unit (often feet!), or name the unit your number is in.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByProximity")]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.GroupResultsByProximity@Autodesk.Navisworks.Api.Clash.ClashTest,double,Autodesk.Navisworks.Api.Document")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeCategory("Navisworks.Clash.Group")]
    [NodeDescription("Groups a test's results into clusters whose clash points lie within a radius of the cluster seed — one issue per hotspot. The radius is in document units unless units names another unit. Rebuilds the test's result tree: every group the test already has (also one made by Clash.GroupResults) is dissolved first and a group's own status, assignee and comments are not kept; a bucket with only one result stays ungrouped (a group needs two or more), so a status held by a single result gets no group. Both are reported as warnings.")]
    [NodeSearchTags("clash", "group", "proximity", "cluster", "radius", "triage")]
    [MultiReturn("test", "groupCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> GroupResultsByProximity(
        ClashTest test,
        [NodeRange(0, 1000000, SoftMin = 0, SoftMax = 10)] double radius,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document",
        Document? document = null)
    {
        if (radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "The cluster radius must be positive.");
        }

        var documentRadius = radius * NavisValues.ResolveUnitsScale(NavisworksContext.ResolveDocument(document), units);
        return ClashRegroup.Commit(test, document, results => PartitionByProximity(results, documentRadius));
    }

    /// <summary>Groups a test's results by building level.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="levelNames">Level names, index-aligned with the elevations.</param>
    /// <param name="levelElevations">Level elevations (Z), in the unit chosen under "units" (document units by default).</param>
    /// <param name="units">Unit of the elevations: "document" uses the file's internal unit (often feet!), or name the unit your numbers are in.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByLevel")]
    [NodeAliases("CamelGraph.Navisworks.ClashNodes.GroupResultsByLevel@Autodesk.Navisworks.Api.Clash.ClashTest,System.Collections.Generic.IEnumerable<string>,System.Collections.Generic.IEnumerable<double>,Autodesk.Navisworks.Api.Document")]
    [NodeFunction(CamelGraph.Core.Graph.NodeFunction.Modify)]
    [NodeEffects(CamelGraph.Core.Graph.NodeEffects.ChangesModel)]
    [NodeCategory("Navisworks.Clash.Group")]
    [NodeDescription("Groups a test's results by nearest level below each clash point (wire your level names and elevations) — per-floor triage. The elevations are in document units unless units names another unit. Rebuilds the test's result tree: every group the test already has (also one made by Clash.GroupResults) is dissolved first and a group's own status, assignee and comments are not kept; a bucket with only one result stays ungrouped (a group needs two or more), so a status held by a single result gets no group. Both are reported as warnings.")]
    [NodeSearchTags("clash", "group", "level", "floor", "storey", "elevation", "triage")]
    [MultiReturn("test", "groupCount")]
    [PortKinds("clash", "integer")]
    public static Dictionary<string, object?> GroupResultsByLevel(
        ClashTest test,
        IEnumerable<string> levelNames,
        IEnumerable<double> levelElevations,
        [NodePanel("Advanced")][NodeChoicesFromEnum(typeof(Units), "document")] string units = "document",
        Document? document = null)
    {
        if (levelNames == null)
        {
            throw new ArgumentNullException(nameof(levelNames), "No level names provided.");
        }

        if (levelElevations == null)
        {
            throw new ArgumentNullException(nameof(levelElevations), "No level elevations provided.");
        }

        var names = new List<string>(levelNames);
        var levelScale = NavisValues.ResolveUnitsScale(NavisworksContext.ResolveDocument(document), units);
        var elevations = new List<double>();
        foreach (var elevation in levelElevations)
        {
            elevations.Add(elevation * levelScale);
        }

        if (names.Count == 0 || names.Count != elevations.Count)
        {
            throw new ArgumentException(
                "Got " + names.Count + " level names but " + elevations.Count +
                " elevations — wire one elevation per level name.", nameof(levelElevations));
        }

        // Sort levels by elevation, keeping names aligned.
        var order = new List<int>();
        for (int i = 0; i < names.Count; i++)
        {
            order.Add(i);
        }

        order.Sort((a, b) => elevations[a].CompareTo(elevations[b]));
        var sortedNames = order.ConvertAll(i => names[i]);
        var sortedElevations = order.ConvertAll(i => elevations[i]);

        return ClashRegroup.Commit(test, document, results => PartitionByLevel(results, sortedNames, sortedElevations));
    }

    private static System.Drawing.Imaging.ImageFormat ImageFormatForExtension(string filePath)
    {
        var extension = (System.IO.Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
        switch (extension)
        {
            case ".png": return System.Drawing.Imaging.ImageFormat.Png;
            case ".jpg":
            case ".jpeg": return System.Drawing.Imaging.ImageFormat.Jpeg;
            case ".bmp": return System.Drawing.Imaging.ImageFormat.Bmp;
            default:
                throw new ArgumentException(
                    "'" + filePath + "' must end in .png, .jpg or .bmp.", nameof(filePath));
        }
    }

    private static List<KeyValuePair<string, List<ClashResult>>> PartitionBySameItem(
        List<ClashResult> results,
        bool useItem1)
    {
        var buckets = new List<KeyValuePair<string, List<ClashResult>>>();
        var keyItems = new List<ModelItem>();
        var bucketsByHash = new Dictionary<int, List<int>>();
        List<ClashResult>? withoutItem = null;

        foreach (var result in results)
        {
            var item = useItem1 ? result.Item1 : result.Item2;
            if (item == null)
            {
                withoutItem = withoutItem ?? new List<ClashResult>();
                withoutItem.Add(result);
                continue;
            }

            var bucketIndex = -1;
            if (bucketsByHash.TryGetValue(item.InstanceHashCode, out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    if (keyItems[candidate].IsSameInstance(item))
                    {
                        bucketIndex = candidate;
                        break;
                    }
                }
            }
            else
            {
                candidates = new List<int>();
                bucketsByHash[item.InstanceHashCode] = candidates;
            }

            if (bucketIndex < 0)
            {
                bucketIndex = buckets.Count;
                var name = item.DisplayName;
                if (string.IsNullOrEmpty(name))
                {
                    name = item.ClassDisplayName;
                }

                if (string.IsNullOrEmpty(name))
                {
                    name = "Item";
                }

                buckets.Add(new KeyValuePair<string, List<ClashResult>>(name, new List<ClashResult>()));
                keyItems.Add(item);
                candidates.Add(bucketIndex);
            }

            buckets[bucketIndex].Value.Add(result);
        }

        if (withoutItem != null)
        {
            buckets.Add(new KeyValuePair<string, List<ClashResult>>("(no item)", withoutItem));
        }

        return buckets;
    }

    private static List<KeyValuePair<string, List<ClashResult>>> PartitionByProximity(
        List<ClashResult> results,
        double radius)
    {
        // Each result joins the first cluster whose seed (its first result) is within the radius; the grid in
        // ProximityClusterer only spares the comparison with seeds that are far away.
        var centers = new Point3D[results.Count];
        var xs = new double[results.Count];
        var ys = new double[results.Count];
        var zs = new double[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            var center = results[i].Center;
            centers[i] = center;
            xs[i] = center.X;
            ys[i] = center.Y;
            zs[i] = center.Z;
        }

        var clusterOf = ProximityClusterer.Assign(
            xs, ys, zs, radius, (seed, point) => centers[seed].DistanceTo(centers[point]) <= radius);

        var clusters = new List<List<ClashResult>>();
        for (int i = 0; i < results.Count; i++)
        {
            if (clusterOf[i] == clusters.Count)
            {
                clusters.Add(new List<ClashResult>());
            }

            clusters[clusterOf[i]].Add(results[i]);
        }

        var buckets = new List<KeyValuePair<string, List<ClashResult>>>(clusters.Count);
        for (int i = 0; i < clusters.Count; i++)
        {
            buckets.Add(new KeyValuePair<string, List<ClashResult>>("Cluster " + (i + 1), clusters[i]));
        }

        return buckets;
    }

    private static List<KeyValuePair<string, List<ClashResult>>> PartitionByLevel(
        List<ClashResult> results,
        List<string> sortedNames,
        List<double> sortedElevations)
    {
        var buckets = new List<KeyValuePair<string, List<ClashResult>>>();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var z = result.Center.Z;

            // Highest level at or below the clash point; below the lowest level
            // still maps to the lowest level.
            var levelIndex = 0;
            for (int i = 0; i < sortedElevations.Count; i++)
            {
                if (sortedElevations[i] <= z + 1e-9)
                {
                    levelIndex = i;
                }
            }

            var name = sortedNames[levelIndex];
            if (!indexByName.TryGetValue(name, out var bucketIndex))
            {
                bucketIndex = buckets.Count;
                indexByName[name] = bucketIndex;
                buckets.Add(new KeyValuePair<string, List<ClashResult>>(name, new List<ClashResult>()));
            }

            buckets[bucketIndex].Value.Add(result);
        }

        return buckets;
    }

    private static List<ClashResult> FlattenResults(ClashTest test)
    {
        var results = new List<ClashResult>();
        CollectResults(test.Children, results);
        return results;
    }

    private static void CollectResults(IEnumerable<SavedItem> items, List<ClashResult> results)
    {
        foreach (var item in items)
        {
            if (item is ClashResult result)
            {
                results.Add(result);
            }
            else if (item is GroupItem group)
            {
                // ClashResultGroup children are the grouped ClashResults.
                CollectResults(group.Children, results);
            }
        }
    }
}
