using System;
using Xunit;
using static CamelGraph.Nodes.Tests.NavisworksSourceText;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so this reads the source of the two nodes that make
/// many saved items in one folder (Viewpoints.FromClashResults and SelectionSets.BulkByPropertyValues) and pins that they no
/// longer search the growing folder twice per item (the unit tests of the name index are what show the new way is right).
/// </summary>
public class FolderFillingNodesSourceTests
{
    [Fact]
    public void ViewpointsFromClashResultsDoesNotSearchTheFolderForEveryResult()
    {
        var body = Body(Source("ViewpointNodes.cs"), "public static List<SavedViewpoint> FromClashResults(");

        Assert.Contains("NavisValues.BuildNameIndex<SavedViewpoint>(", body);
        Assert.Contains("NavisValues.ConfirmStored<SavedViewpoint>(", body);
        Assert.DoesNotContain("FindTopLevelIndex<SavedViewpoint>(children", body);

        // The index is built before the loop, not inside it.
        Assert.True(body.IndexOf("BuildNameIndex<SavedViewpoint>(", StringComparison.Ordinal) < body.IndexOf("foreach (var result in results)", StringComparison.Ordinal));
    }

    [Fact]
    public void BulkByPropertyValuesDoesNotSearchTheTargetForEveryValue()
    {
        var source = Source("SelectionSetNodes.cs");
        var bulk = Body(source, "public static Dictionary<string, object?> BulkByPropertyValues(");

        Assert.Contains("NavisValues.BuildNameIndex<SelectionSet>(", bulk);
        Assert.True(bulk.IndexOf("BuildNameIndex<SelectionSet>(", StringComparison.Ordinal) < bulk.IndexOf("foreach (var value in values)", StringComparison.Ordinal));
        Assert.Contains("StoreTopLevel(doc, set, value, names)", bulk);
        Assert.Contains("StoreInFolder(doc, parentFolder, set, value, names)", bulk);

        foreach (var store in new[] { "private static SelectionSet StoreTopLevel(", "private static SelectionSet StoreInFolder(" })
        {
            var body = Body(source, store);
            Assert.DoesNotContain("FindTopLevelIndex", body);
            Assert.Contains("NavisValues.ConfirmStored<SelectionSet>(", body);
        }
    }
}
