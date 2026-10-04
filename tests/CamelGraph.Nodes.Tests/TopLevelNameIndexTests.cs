using System;
using System.Collections.Generic;
using CamelGraph.Nodes.Portable;
using Xunit;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The name index behind Viewpoints.FromClashResults and SelectionSets.BulkByPropertyValues: it must answer "is there an item
/// with this name, and where" exactly as a scan of the list for the first item of the kind with that name would, while
/// the list grows.
/// </summary>
public class TopLevelNameIndexTests
{
    // The old way: scan the list for the first item of the kind with the name.
    private static int Scan(List<string?> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public void AnEmptyIndexFindsNothing()
    {
        var index = new TopLevelNameIndex();

        Assert.Equal(0, index.Count);
        Assert.False(index.TryGetIndex("a", out var position));
        Assert.Equal(-1, position);
    }

    [Fact]
    public void TheFirstItemWithANameIsTheOneFound()
    {
        var index = new TopLevelNameIndex(new string?[] { "a", "b", "a", "c", "b" });

        Assert.Equal(5, index.Count);
        Assert.True(index.TryGetIndex("a", out var a));
        Assert.Equal(0, a);
        Assert.True(index.TryGetIndex("b", out var b));
        Assert.Equal(1, b);
        Assert.True(index.TryGetIndex("c", out var c));
        Assert.Equal(3, c);
    }

    [Fact]
    public void NamesAreComparedExactlyCaseIncluded()
    {
        var index = new TopLevelNameIndex(new string?[] { "Clash1" });

        Assert.False(index.TryGetIndex("clash1", out _));
        Assert.False(index.TryGetIndex("Clash1 ", out _));
        Assert.True(index.TryGetIndex("Clash1", out _));
    }

    [Fact]
    public void AnItemOfAnotherKindTakesAPositionButIsNeverFound()
    {
        // a folder named like a viewpoint, then the viewpoint
        var index = new TopLevelNameIndex(new string?[] { null, "Folder", null, "View" });

        Assert.Equal(4, index.Count);
        Assert.True(index.TryGetIndex("View", out var view));
        Assert.Equal(3, view);
        Assert.False(index.TryGetIndex(string.Empty, out _));
    }

    [Fact]
    public void AnAppendedItemIsFoundAtTheEndAndAnOlderNameKeepsItsPosition()
    {
        var index = new TopLevelNameIndex(new string?[] { "a", "b" });

        Assert.Equal(2, index.Append("c"));
        Assert.Equal(3, index.Append("a"));

        Assert.Equal(4, index.Count);
        Assert.True(index.TryGetIndex("c", out var c));
        Assert.Equal(2, c);
        Assert.True(index.TryGetIndex("a", out var a));
        Assert.Equal(0, a);
    }

    [Fact]
    public void ResetIndexesTheListAgain()
    {
        var index = new TopLevelNameIndex(new string?[] { "a", "b" });

        index.Reset(new string?[] { "x", null, "b" });

        Assert.Equal(3, index.Count);
        Assert.False(index.TryGetIndex("a", out _));
        Assert.True(index.TryGetIndex("b", out var b));
        Assert.Equal(2, b);
    }

    [Fact]
    public void ANullListIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new TopLevelNameIndex(null!));
    }

    [Fact]
    public void WhileAListGrowsTheIndexAnswersLikeAScanOfTheList()
    {
        // The node's loop: look the name up, replace the item or append a new one, repeat.
        for (int seed = 0; seed < 100; seed++)
        {
            var random = new Random(seed);
            var list = new List<string?>();
            for (int i = random.Next(0, 8); i > 0; i--)
            {
                list.Add(random.Next(4) == 0 ? null : "n" + random.Next(10));
            }

            var index = new TopLevelNameIndex(list);
            for (int step = 0; step < 60; step++)
            {
                var name = "n" + random.Next(14);
                var expected = Scan(list, name);
                var found = index.TryGetIndex(name, out var position);

                Assert.True(found == (expected >= 0), "seed " + seed + " step " + step);
                Assert.Equal(expected, position);

                if (found)
                {
                    list[position] = name; // replaced in place
                }
                else
                {
                    Assert.Equal(list.Count, index.Append(name));
                    list.Add(name);
                }
            }

            Assert.Equal(list.Count, index.Count);
        }
    }
}
