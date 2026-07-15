using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Dyncamelo.Core.Loader;
using Dyncamelo.Navisworks.Internal;

namespace Dyncamelo.Navisworks;

/// <summary>Nodes for reading Clash Detective tests and results.</summary>
[NodeCategory("Navisworks.Clash")]
public static class ClashNodes
{
    /// <summary>All clash tests in a document.</summary>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>Every clash test, including those nested in folders.</returns>
    [NodeName("Clash.Tests")]
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
    /// <returns>Name, status, type, tolerance, last-run time and result count.</returns>
    [NodeName("ClashTest.Info")]
    [NodeDescription("Name, status, type, tolerance, last run time and result count of a clash test.")]
    [NodeSearchTags("clash", "test", "info", "status", "tolerance")]
    [MultiReturn("name", "status", "testType", "tolerance", "lastRun", "resultCount")]
    public static Dictionary<string, object?> Info(ClashTest test)
    {
        var clashTest = RequireTest(test);
        return new Dictionary<string, object?>
        {
            ["name"] = clashTest.DisplayName,
            ["status"] = clashTest.Status.ToString(),
            ["testType"] = clashTest.TestType.ToString(),
            ["tolerance"] = clashTest.Tolerance,
            ["lastRun"] = clashTest.LastRun,
            ["resultCount"] = FlattenResults(clashTest).Count,
        };
    }

    /// <summary>The individual clash results of a test.</summary>
    /// <param name="test">The clash test.</param>
    /// <returns>Every result, with grouped results flattened.</returns>
    [NodeName("ClashTest.Results")]
    [NodeDescription("The individual results of a clash test (grouped results are flattened).")]
    [NodeSearchTags("clash", "test", "results", "clashes")]
    [return: NodeName("results")]
    public static List<ClashResult> Results(ClashTest test)
    {
        return FlattenResults(RequireTest(test));
    }

    /// <summary>Summary information about a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>Name, status, distance, description, assignee and creation time.</returns>
    [NodeName("ClashResult.Info")]
    [NodeDescription("Name, status, distance, description, assignee and creation time of a clash result.")]
    [NodeSearchTags("clash", "result", "info", "status", "distance")]
    [MultiReturn("name", "status", "distance", "description", "assignedTo", "createdTime")]
    public static Dictionary<string, object?> ResultInfo(ClashResult result)
    {
        var clashResult = RequireResult(result);
        return new Dictionary<string, object?>
        {
            ["name"] = clashResult.DisplayName,
            ["status"] = clashResult.Status.ToString(),
            ["distance"] = clashResult.Distance,
            ["description"] = clashResult.Description,
            ["assignedTo"] = clashResult.AssignedTo,
            ["createdTime"] = clashResult.CreatedTime,
        };
    }

    /// <summary>The two model items involved in a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The clashing items.</returns>
    [NodeName("ClashResult.Items")]
    [NodeDescription("The two model items involved in a clash result.")]
    [NodeSearchTags("clash", "result", "items", "elements", "pair")]
    [MultiReturn("item1", "item2")]
    public static Dictionary<string, object?> ResultItems(ClashResult result)
    {
        var clashResult = RequireResult(result);
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
    [NodeDescription("The clash point of a result, in document units.")]
    [NodeSearchTags("clash", "result", "center", "point", "location")]
    [return: NodeName("point")]
    public static Point3D Center(ClashResult result)
    {
        return RequireResult(result).Center;
    }

    /// <summary>The crossing angle between the two clashing elements.</summary>
    /// <param name="result">The clash result.</param>
    /// <returns>The angle in degrees (0–90) between the two elements' overall directions.</returns>
    [NodeName("ClashResult.Angle")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription(
        "The angle in degrees (0–90) between the two clashing elements, taken from each element's overall " +
        "direction (its bounding-box diagonal). ~0° means they run parallel/together, ~90° means they cross " +
        "perpendicular — so you can filter clashes by crossing angle (e.g. keep only ~90° crossings). Returns " +
        "NaN when an element has no measurable direction (a point-like box).")]
    [NodeSearchTags("clash", "angle", "degrees", "perpendicular", "crossing", "orientation", "direction")]
    [return: NodeName("degrees")]
    public static double Angle(ClashResult result)
    {
        var clashResult = RequireResult(result);
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("The size of the clash overlap region — its bounding-box volume in document units³. Filter out tiny grazing clashes and keep the significant ones. Pair with Units.Convert for readable units.")]
    [NodeSearchTags("clash", "size", "volume", "extent", "significance", "big", "small", "filter")]
    [return: NodeName("volume")]
    public static double Size(ClashResult result)
    {
        var box = RequireResult(result).BoundingBox;
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("How documented a clash already is — whether it has a saved viewpoint, redline markup, and how many comments. Filter to the reviewed/annotated ones, or find the ones still needing attention (commentCount = 0).")]
    [NodeSearchTags("clash", "comments", "viewpoint", "redline", "reviewed", "documented", "annotated", "filter")]
    [MultiReturn("hasViewpoint", "hasRedlines", "commentCount")]
    public static Dictionary<string, object?> Documentation(ClashResult result)
    {
        var clashResult = RequireResult(result);
        return new Dictionary<string, object?>
        {
            ["hasViewpoint"] = clashResult.HasSavedViewpoint,
            ["hasRedlines"] = clashResult.HasRedlines,
            ["commentCount"] = clashResult.Comments?.Count ?? 0,
        };
    }

    /// <summary>Keeps only the clash results with a given status.</summary>
    /// <param name="results">The clash results (e.g. from ClashTest.Results).</param>
    /// <param name="status">New, Active, Reviewed, Approved or Resolved.</param>
    /// <returns>The matching results, input order preserved.</returns>
    [NodeName("Clash.FilterByStatus")]
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Keeps only the clash results with the given status (New, Active, Reviewed, Approved or Resolved) — the triage staple: e.g. drop the already-Approved ones and work the New ones.")]
    [NodeSearchTags("clash", "filter", "status", "new", "active", "approved", "resolved", "triage")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByStatus(
        IEnumerable<ClashResult> results,
        [NodeChoices("New", "Active", "Reviewed", "Approved", "Resolved")]
        string status)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results), "No clash results provided.");
        }

        var target = ParseResultStatus(status);
        var matched = new List<ClashResult>();
        foreach (var result in results)
        {
            if (result != null && result.Status == target)
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
    [NodeFunction(Dyncamelo.Core.Graph.NodeFunction.Info)]
    [NodeDescription("Keeps only the clash results whose crossing angle (see ClashResult.Angle) is within a degree range — e.g. 80–90 for near-perpendicular crossings, or 0–10 for parallel runs. Results with no measurable direction are dropped.")]
    [NodeSearchTags("clash", "filter", "angle", "perpendicular", "parallel", "crossing", "degrees")]
    [return: NodeName("results")]
    public static List<ClashResult> FilterByAngle(
        IEnumerable<ClashResult> results,
        double minDegrees = 0.0,
        double maxDegrees = 90.0)
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

    /// <summary>Creates (or replaces) a clash test between two item selections.</summary>
    /// <param name="name">Display name for the test.</param>
    /// <param name="itemsA">Selection A (e.g. from a search or selection set).</param>
    /// <param name="itemsB">Selection B.</param>
    /// <param name="testType">One of: Hard, HardConservative, Clearance, Duplicate, Custom.</param>
    /// <param name="tolerance">Tolerance in document units (Clearance distance for clearance tests).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The stored clash test, ready for ClashTest.Run.</returns>
    [NodeName("ClashTest.Create")]
    [NodeDescription("Creates a clash test between two item selections — script the weekly test matrix instead of clicking it. An existing top-level test with the same name is replaced.")]
    [NodeSearchTags("clash", "test", "create", "new", "setup", "matrix")]
    [return: NodeName("test")]
    public static ClashTest Create(
        string name,
        IEnumerable<ModelItem> itemsA,
        IEnumerable<ModelItem> itemsB,
        [NodeChoices("Hard", "HardConservative", "Clearance", "Duplicate", "Custom")]
        string testType = "Hard",
        double tolerance = 0.01,
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

        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var test = new ClashTest
        {
            DisplayName = name,
            TestType = ClashHelpers.ParseTestType(testType),
            Tolerance = tolerance,
        };
        test.SelectionA.Selection.CopyFrom(NavisValues.ToItemCollection(itemsA));
        test.SelectionB.Selection.CopyFrom(NavisValues.ToItemCollection(itemsB));

        // Re-running a graph should update the test, not pile up duplicates.
        var tests = clash.TestsData;
        var existingIndex = NavisValues.FindTopLevelIndex<ClashTest>(tests.Tests, name);
        if (existingIndex >= 0)
        {
            tests.TestsReplaceWithCopy(existingIndex, test);
        }
        else
        {
            tests.TestsAddCopy(test);
        }

        // AddCopy/ReplaceWithCopy store a copy — hand the stored instance downstream.
        var storedIndex = NavisValues.FindTopLevelIndex<ClashTest>(tests.Tests, name);
        return storedIndex >= 0 ? (ClashTest)tests.Tests[storedIndex] : test;
    }

    /// <summary>Runs one clash test now.</summary>
    /// <param name="test">The stored clash test (from Clash.Tests, ClashTest.ByName or ClashTest.Create).</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The test (pass-through) and its result count after the run.</returns>
    [NodeName("ClashTest.Run")]
    [NodeDescription("Runs one clash test now and reports the result count.")]
    [NodeSearchTags("clash", "test", "run", "execute", "detect")]
    [MultiReturn("test", "resultCount")]
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
    /// <param name="status">One of: New, Active, Reviewed, Approved, Resolved.</param>
    /// <returns>The results with that status (grouped results are flattened).</returns>
    [NodeName("ClashTest.ResultsByStatus")]
    [NodeDescription("The results of a test that have the given status (New/Active/Reviewed/Approved/Resolved).")]
    [NodeSearchTags("clash", "results", "status", "filter", "triage")]
    [return: NodeName("results")]
    public static List<ClashResult> ResultsByStatus(ClashTest test, string status)
    {
        var wanted = ClashHelpers.ParseResultStatus(status);
        return ClashHelpers.FlattenResults(RequireTest(test)).FindAll(r => r.Status == wanted);
    }

    /// <summary>Sets the status of a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="status">One of: New, Active, Reviewed, Approved, Resolved.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The result (pass-through). Lace over result lists for bulk triage.</returns>
    [NodeName("ClashResult.SetStatus")]
    [NodeDescription("Sets a clash result's status — with lacing this is bulk triage by rule (e.g. distance < 10 mm → Reviewed).")]
    [NodeSearchTags("clash", "result", "status", "set", "resolve", "approve", "triage")]
    [return: NodeName("result")]
    public static ClashResult SetStatus(ClashResult result, string status, Document? document = null)
    {
#if !NAV2026
        var clashResult = RequireResult(result);
        var wanted = ClashHelpers.ParseResultStatus(status);
        var doc = NavisworksContext.ResolveDocument(document);
        ClashHelpers.RequireClash(doc).TestsData.TestsEditResultStatus(clashResult, wanted);
        return clashResult;
#else
        // TestsEditResultStatus gained a required Assignee (current-user) argument in
        // Navisworks 2026; pending a port verified on that release.
        throw new System.NotSupportedException(
            "ClashResult.SetStatus is currently supported on Navisworks 2024 and 2025; " +
            "the 2026 clash-status API differs and this node is pending an update.");
#endif
    }

    /// <summary>Assigns a clash result to a person or trade.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="assignedTo">The assignee (e.g. "MEP", "j.smith").</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The result (pass-through). Lace over result lists for bulk assignment.</returns>
    [NodeName("ClashResult.Assign")]
    [NodeDescription("Assigns a clash result to a person or trade — bulk assignment via lacing.")]
    [NodeSearchTags("clash", "result", "assign", "trade", "responsible")]
    [return: NodeName("result")]
    public static ClashResult Assign(ClashResult result, string assignedTo, Document? document = null)
    {
#if !NAV2026
        var clashResult = RequireResult(result);
        var doc = NavisworksContext.ResolveDocument(document);
        ClashHelpers.RequireClash(doc).TestsData.TestsEditResultAssignedTo(clashResult, assignedTo ?? string.Empty);
        return clashResult;
#else
        // TestsEditResultAssignedTo takes an Assignee (not a string) in Navisworks 2026;
        // pending a port verified on that release.
        throw new System.NotSupportedException(
            "ClashResult.Assign is currently supported on Navisworks 2024 and 2025; " +
            "the 2026 clash-assignee API differs and this node is pending an update.");
#endif
    }

    /// <summary>Sets the description of a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="description">The description text.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The result (pass-through).</returns>
    [NodeName("ClashResult.SetDescription")]
    [NodeDescription("Sets a clash result's description text (context for reports and reviews).")]
    [NodeSearchTags("clash", "result", "description", "set", "note")]
    [return: NodeName("result")]
    public static ClashResult SetDescription(ClashResult result, string description, Document? document = null)
    {
        var clashResult = RequireResult(result);
        var doc = NavisworksContext.ResolveDocument(document);
        ClashHelpers.RequireClash(doc).TestsData.TestsEditResultDescription(clashResult, description ?? string.Empty);
        return clashResult;
    }

    /// <summary>The auto-generated camera viewpoint of a clash result.</summary>
    /// <param name="result">The clash result.</param>
    /// <param name="apply">True to also make it the current view.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The viewpoint aimed at the clash.</returns>
    [NodeName("ClashResult.Viewpoint")]
    [NodeDescription("The camera viewpoint Navisworks generates for a clash result; optionally applies it to the current view.")]
    [NodeSearchTags("clash", "result", "viewpoint", "camera", "goto")]
    [return: NodeName("viewpoint")]
    public static Viewpoint ResultViewpoint(ClashResult result, bool apply = false, Document? document = null)
    {
        var clashResult = RequireResult(result);
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
    [NodeDescription("Renders a clash snapshot (scene plus clash highlight) to a .png/.jpg/.bmp file — the picture half of every clash report.")]
    [NodeSearchTags("clash", "result", "image", "snapshot", "screenshot", "report")]
    [return: NodeName("filePath")]
    public static string SaveImage(
        ClashResult result,
        string filePath,
        int width = 1280,
        int height = 720,
        Document? document = null)
    {
        var clashResult = RequireResult(result);
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("No file path provided.", nameof(filePath));
        }

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
    [NodeDescription("Groups a test's results so every clash involving the same element lands in one group (named after the element) — turns thousands of raw clashes into one issue per element.")]
    [NodeSearchTags("clash", "group", "same", "item", "element", "triage")]
    [MultiReturn("test", "groupCount")]
    public static Dictionary<string, object?> GroupResultsBySameItem(
        ClashTest test,
        bool useItem1 = true,
        Document? document = null)
    {
        return CommitRegroup(test, document, results => PartitionBySameItem(results, useItem1));
    }

    /// <summary>Groups a test's results into clusters of nearby clash points.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="radius">Cluster radius in document units.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByProximity")]
    [NodeDescription("Groups a test's results into clusters whose clash points lie within a radius of the cluster seed — one issue per hotspot.")]
    [NodeSearchTags("clash", "group", "proximity", "cluster", "radius", "triage")]
    [MultiReturn("test", "groupCount")]
    public static Dictionary<string, object?> GroupResultsByProximity(
        ClashTest test,
        double radius,
        Document? document = null)
    {
        if (radius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "The cluster radius must be positive (in document units).");
        }

        return CommitRegroup(test, document, results => PartitionByProximity(results, radius));
    }

    /// <summary>Groups a test's results by building level.</summary>
    /// <param name="test">The stored clash test.</param>
    /// <param name="levelNames">Level names, index-aligned with the elevations.</param>
    /// <param name="levelElevations">Level elevations (Z) in document units.</param>
    /// <param name="document">The document (defaults to the active document).</param>
    /// <returns>The regrouped test and the number of groups created.</returns>
    [NodeName("Clash.GroupResultsByLevel")]
    [NodeDescription("Groups a test's results by nearest level below each clash point (wire your level names and elevations) — per-floor triage.")]
    [NodeSearchTags("clash", "group", "level", "floor", "storey", "elevation", "triage")]
    [MultiReturn("test", "groupCount")]
    public static Dictionary<string, object?> GroupResultsByLevel(
        ClashTest test,
        IEnumerable<string> levelNames,
        IEnumerable<double> levelElevations,
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
        var elevations = new List<double>(levelElevations);
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

        return CommitRegroup(test, document, results => PartitionByLevel(results, sortedNames, sortedElevations));
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

    /// <summary>
    /// Rebuilds a stored test's result tree from a partition: flattens the current
    /// results, buckets them, wraps buckets of two or more into named
    /// ClashResultGroups (singletons stay ungrouped) and commits the new tree in
    /// one TestsEditTestFromCopy edit.
    /// </summary>
    private static Dictionary<string, object?> CommitRegroup(
        ClashTest test,
        Document? document,
        Func<List<ClashResult>, List<KeyValuePair<string, List<ClashResult>>>> partition)
    {
        var stored = ClashHelpers.RequireStoredTest(test);
        var doc = NavisworksContext.ResolveDocument(document);
        var clash = ClashHelpers.RequireClash(doc);

        var copy = (ClashTest)stored.CreateCopy();
        var flattened = ClashHelpers.FlattenResults(copy);

        // Detach every result from the copy's tree before rebuilding it — Clear()
        // below destroys the originals.
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
        var seeds = new List<Point3D>();
        var clusters = new List<List<ClashResult>>();
        foreach (var result in results)
        {
            var center = result.Center;
            var clusterIndex = -1;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (seeds[i].DistanceTo(center) <= radius)
                {
                    clusterIndex = i;
                    break;
                }
            }

            if (clusterIndex < 0)
            {
                clusterIndex = seeds.Count;
                seeds.Add(center);
                clusters.Add(new List<ClashResult>());
            }

            clusters[clusterIndex].Add(result);
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

    private static ClashTest RequireTest(ClashTest? test)
    {
        return test ?? throw new ArgumentNullException(nameof(test), "No clash test provided.");
    }

    private static ClashResult RequireResult(ClashResult? result)
    {
        return result ?? throw new ArgumentNullException(nameof(result), "No clash result provided.");
    }
}
