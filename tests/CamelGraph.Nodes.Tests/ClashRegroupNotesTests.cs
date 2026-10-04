using System.Collections.Generic;
using CamelGraph.Nodes.Coordination;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>What the regrouping nodes say about the groups they dissolved and the results they left loose (NVC-10).</summary>
public class ClashRegroupNotesTests
{
    [Fact]
    public void ARegroupThatSurprisesNobodySaysNothing()
    {
        Assert.Empty(ClashRegroupNotes.Build(0, new List<string>(), 0));
    }

    [Fact]
    public void DissolvedGroupsAreCountedAndTheLossOfTheirOwnStateIsSaid()
    {
        var notes = ClashRegroupNotes.Build(3, new List<string>(), 0);

        var note = Assert.Single(notes);
        Assert.Contains("3 groups", note);
        Assert.Contains("dissolved", note);
        Assert.Contains("status, assignee and comments are not kept", note);
    }

    [Fact]
    public void OneGroupIsNotPluralised()
    {
        Assert.Contains("1 group;", ClashRegroupNotes.Build(1, new List<string>(), 0)[0]);
    }

    [Fact]
    public void ALooseResultNamesItsBucketAndWhyItHasNoGroup()
    {
        var notes = ClashRegroupNotes.Build(0, new List<string> { "Approved" }, 1);

        var note = Assert.Single(notes);
        Assert.Equal("1 result ('Approved') was alone in its bucket and stays ungrouped: a group needs two or more results.", note);
    }

    [Fact]
    public void ManyLooseBucketsShowTheFirstThreeNamesAndCountTheRest()
    {
        var notes = ClashRegroupNotes.Build(0, new List<string> { "A", "B", "C", "D", "E" }, 5);

        var note = Assert.Single(notes);
        Assert.Contains("5 results ('A', 'B', 'C' and 2 more) were alone in their buckets and stay ungrouped", note);
    }

    [Fact]
    public void BothNotesComeTogetherInOrder()
    {
        var notes = ClashRegroupNotes.Build(2, new List<string> { "Resolved" }, 1);

        Assert.Equal(2, notes.Count);
        Assert.Contains("dissolved", notes[0]);
        Assert.Contains("ungrouped", notes[1]);
    }
}
