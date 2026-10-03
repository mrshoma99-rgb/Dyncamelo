using System.Text.RegularExpressions;
using Xunit;
using static Dyncamelo.Nodes.Tests.NavisworksSourceText;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// The Navisworks node project cannot be loaded by the Linux test projects, so this reads the source of the nodes that regroup a
/// whole clash test and pins that every one of them commits the rebuilt results tree the way that works
/// (TestsReplaceWithCopy, through ClashHelpers.CommitTestTree): the older TestsEditTestFromCopy applies only a test's own
/// settings and ignores the results tree, which is what the 0.31.2 fix found and what three of the nodes still did.
/// </summary>
public class ClashRegroupSourceTests
{
    [Theory]
    [InlineData("ClashNodes.cs")]
    [InlineData("ClashEditNodes.cs")]
    [InlineData("ClashTriageNodes.cs")]
    public void NoGroupingNodeCommitsAResultsTreeWithTestsEditTestFromCopy(string file)
    {
        Assert.DoesNotMatch(@"\.TestsEditTestFromCopy\(", Source(file));
    }

    [Fact]
    public void TheRegroupingNodesAllCommitThroughTheSameHelper()
    {
        var clashNodes = Source("ClashNodes.cs");
        var editNodes = Source("ClashEditNodes.cs");

        foreach (var node in new[] { "GroupResultsBySameItem", "GroupResultsByProximity", "GroupResultsByLevel" })
        {
            Assert.Contains("ClashRegroup.Commit(", Body(clashNodes, "public static Dictionary<string, object?> " + node + "("));
        }

        foreach (var node in new[] { "GroupResultsByStatus", "GroupResultsByGridIntersection" })
        {
            Assert.Contains("ClashRegroup.Commit(", Body(editNodes, "public static Dictionary<string, object?> " + node + "("));
        }

        var regroup = Source("Internal", "ClashRegroup.cs");
        Assert.Contains("ClashHelpers.CommitTestTree(", regroup);
        Assert.Contains("StaleInputError(", regroup);
        Assert.Contains("[\"test\"] = refreshed", regroup);
        Assert.DoesNotMatch(@"\[""test""\] = stored", regroup);

        Assert.Contains("TestsReplaceWithCopy(", Source("Internal", "ClashHelpers.cs"));
        Assert.DoesNotContain("CommitRegroup(", clashNodes);
        Assert.DoesNotContain("RegroupAndCommit(", editNodes);
    }

    [Fact]
    public void TheProximityClusteringUsesTheGridClusterer()
    {
        var body = Body(Source("ClashNodes.cs"), "private static List<KeyValuePair<string, List<ClashResult>>> PartitionByProximity(");

        Assert.Contains("ProximityClusterer.Assign(", body);
        Assert.DoesNotContain("DistanceTo(center)", body);
    }
}
