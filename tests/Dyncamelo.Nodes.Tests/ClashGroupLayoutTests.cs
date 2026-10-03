using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Nodes.Coordination;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// How a test's results are laid out after one of the regrouping nodes (Clash.GroupResultsByStatus, ...ByGridIntersection,
/// ...BySameItem, ...ByProximity, ...ByLevel) has bucketed them: groups of two or more with unique names, the rest loose.
/// </summary>
public class ClashGroupLayoutTests
{
    [Fact]
    public void BucketsOfTwoOrMoreBecomeGroupsAndSinglesStayLoose()
    {
        var buckets = new List<KeyValuePair<string, List<int>>>
        {
            new KeyValuePair<string, List<int>>("Active", new List<int> { 1, 2, 3 }),
            new KeyValuePair<string, List<int>>("New", new List<int> { 4 }),
            new KeyValuePair<string, List<int>>("Approved", new List<int> { 5, 6 }),
            new KeyValuePair<string, List<int>>("Resolved", new List<int> { 7 }),
        };

        var layout = ClashGroupLayout.Build(buckets);

        Assert.Equal(new[] { "Active", "Approved" }, layout.Groups.Select(g => g.Key));
        Assert.Equal(new[] { 1, 2, 3 }, layout.Groups[0].Value);
        Assert.Equal(new[] { 5, 6 }, layout.Groups[1].Value);
        Assert.Equal(new[] { 4, 7 }, layout.Singles);
    }

    [Fact]
    public void ANameThatIsTakenGetsANumberedSuffix()
    {
        var buckets = new List<KeyValuePair<string, List<string>>>
        {
            new KeyValuePair<string, List<string>>("Pipe", new List<string> { "a", "b" }),
            new KeyValuePair<string, List<string>>("Pipe", new List<string> { "c", "d" }),
            new KeyValuePair<string, List<string>>("Pipe", new List<string> { "e", "f" }),
            new KeyValuePair<string, List<string>>("Pipe (2)", new List<string> { "g", "h" }),
        };

        var layout = ClashGroupLayout.Build(buckets);

        Assert.Equal(new[] { "Pipe", "Pipe (2)", "Pipe (3)", "Pipe (2) (2)" }, layout.Groups.Select(g => g.Key));
    }

    [Fact]
    public void NoBucketsMeansNoGroupsAndNoSingles()
    {
        var layout = ClashGroupLayout.Build(new List<KeyValuePair<string, List<int>>>());

        Assert.Empty(layout.Groups);
        Assert.Empty(layout.Singles);
    }
}
