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
