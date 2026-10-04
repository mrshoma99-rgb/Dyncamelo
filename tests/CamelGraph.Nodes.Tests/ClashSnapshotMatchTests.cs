using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Which live clash results a saved snapshot already had: the "new this week" list as live results (NVC-38).</summary>
public class ClashSnapshotMatchTests
{
    [Fact]
    public void AResultThatIsInTheSnapshotPersistsAndOneThatIsNotIsNew()
    {
        var flags = ClashSnapshotMatch.WasInSnapshot(new[] { "t|a|b", "t|c|d" }, new[] { "t|c|d", "t|e|f", "t|a|b" });

        Assert.Equal(new[] { true, false, true }, flags);
    }

    [Fact]
    public void AnEmptySnapshotMakesEverythingNew()
    {
        Assert.Equal(new[] { false, false }, ClashSnapshotMatch.WasInSnapshot(new string[0], new[] { "x", "y" }));
    }

    [Fact]
    public void NoLiveResultsGiveNoFlags()
    {
        Assert.Empty(ClashSnapshotMatch.WasInSnapshot(new[] { "x" }, new string[0]));
    }

    [Fact]
    public void TheSamePairClashingSeveralTimesIsMatchedOneForOneInOrder()
    {
        // The snapshot had two clashes of this pair; now there are three: the first two persist, the third is new.
        var flags = ClashSnapshotMatch.WasInSnapshot(new[] { "k", "k" }, new[] { "k", "k", "k" });

        Assert.Equal(new[] { true, true, false }, flags);
    }

    [Fact]
    public void TheKeysAreCaseSensitiveLikeTheSnapshotComparison()
    {
        Assert.Equal(new[] { false }, ClashSnapshotMatch.WasInSnapshot(new[] { "T|a|b" }, new[] { "t|a|b" }));
    }
}
