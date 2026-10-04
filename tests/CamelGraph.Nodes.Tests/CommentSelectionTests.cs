using System;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>Which comments of a thread SavedItem.SetCommentStatus changes.</summary>
public class CommentSelectionTests
{
    [Fact]
    public void AnyNegativeIndexMeansEveryComment()
    {
        Assert.Equal(new[] { 0, 1, 2 }, CommentSelection.Positions(3, -1));
        Assert.Equal(new[] { 0, 1 }, CommentSelection.Positions(2, -5));
    }

    [Fact]
    public void AnIndexMeansThatOneComment()
    {
        Assert.Equal(new[] { 2 }, CommentSelection.Positions(3, 2));
        Assert.Equal(new[] { 0 }, CommentSelection.Positions(1, 0));
    }

    [Fact]
    public void AnEmptyThreadHasNothingToChangeForAllComments()
    {
        Assert.Empty(CommentSelection.Positions(0, -1));
    }

    [Fact]
    public void APositionPastTheEndSaysHowManyCommentsThereAre()
    {
        var error = Assert.Throws<ArgumentException>(() => CommentSelection.Positions(3, 3));

        Assert.Contains("3 comment(s)", error.Message);
        Assert.Contains("positions 0 to 2", error.Message);
        Assert.Contains("Use -1", error.Message);
    }

    [Fact]
    public void APositionInAnEmptyThreadSaysThereAreNoComments()
    {
        var error = Assert.Throws<ArgumentException>(() => CommentSelection.Positions(0, 0));

        Assert.Contains("no comments", error.Message);
    }
}
