using System.Collections.Generic;
using Dyncamelo.Core.Graph;
using Xunit;

namespace Dyncamelo.Core.Tests;

/// <summary>
/// Pins the tidy-up layout behind "Arrange Selection": dependency columns left
/// to right, no overlaps, stacking order preserved, and defensive handling of
/// cycles and foreign edges.
/// </summary>
public class GraphLayoutTests
{
    private static GraphLayout.LayoutItem Item(string key, double w = 200, double h = 100) =>
        new GraphLayout.LayoutItem(key, w, h);

    [Fact]
    public void Chain_IsLaidOutLeftToRight_OneColumnPerStep()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var edges = new[] { ((object)"a", (object)"b"), ((object)"b", (object)"c") };

        var placed = GraphLayout.Arrange(items, edges, originX: 0, originY: 0, columnGap: 80, rowGap: 40);

        Assert.Equal(0, placed["a"].X);
        Assert.Equal(280, placed["b"].X);   // 200 wide + 80 gap
        Assert.Equal(560, placed["c"].X);
        // A single-node column centres on the origin line.
        Assert.Equal(-50, placed["a"].Y);
    }

    [Fact]
    public void IndependentNodes_ShareAColumn_AndDoNotOverlap()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };

        var placed = GraphLayout.Arrange(items, new List<(object, object)>(), 0, 0, 80, 40);

        Assert.Equal(placed["a"].X, placed["b"].X);
        Assert.Equal(placed["a"].X, placed["c"].X);
        // Stacked in input order, each a full height + gap apart.
        Assert.Equal(placed["a"].Y + 140, placed["b"].Y);
        Assert.Equal(placed["b"].Y + 140, placed["c"].Y);
    }

    [Fact]
    public void InputOrder_DecidesStackingOrder_SoTheUsersReadingOrderSurvives()
    {
        var forward = GraphLayout.Arrange(new[] { Item("top"), Item("bottom") }, new List<(object, object)>(), 0, 0);
        Assert.True(forward["top"].Y < forward["bottom"].Y);

        var reversed = GraphLayout.Arrange(new[] { Item("bottom"), Item("top") }, new List<(object, object)>(), 0, 0);
        Assert.True(reversed["bottom"].Y < reversed["top"].Y);
    }

    [Fact]
    public void ColumnWidth_FollowsTheWidestNodeInThatColumn()
    {
        var items = new[] { Item("wide", w: 400), Item("narrow", w: 100), Item("next") };
        var edges = new[] { ((object)"wide", (object)"next") };

        var placed = GraphLayout.Arrange(items, edges, 0, 0, columnGap: 80, rowGap: 40);

        Assert.Equal(0, placed["wide"].X);
        Assert.Equal(0, placed["narrow"].X);      // same column
        Assert.Equal(480, placed["next"].X);      // 400 (widest) + 80
    }

    [Fact]
    public void Diamond_PutsTheJoinNodeAfterBothBranches()
    {
        var items = new[] { Item("start"), Item("up"), Item("down"), Item("join") };
        var edges = new[]
        {
            ((object)"start", (object)"up"), ((object)"start", (object)"down"),
            ((object)"up", (object)"join"), ((object)"down", (object)"join"),
        };

        var placed = GraphLayout.Arrange(items, edges, 0, 0);

        Assert.True(placed["start"].X < placed["up"].X);
        Assert.Equal(placed["up"].X, placed["down"].X);
        Assert.True(placed["join"].X > placed["up"].X);
    }

    [Fact]
    public void NothingOverlaps_InADenseArrangement()
    {
        var items = new List<GraphLayout.LayoutItem>();
        for (int i = 0; i < 12; i++)
        {
            items.Add(Item("n" + i, w: 150 + (i * 10), h: 60 + (i * 5)));
        }

        var edges = new List<(object, object)>
        {
            ("n0", "n3"), ("n1", "n3"), ("n3", "n7"), ("n2", "n7"), ("n7", "n11"),
        };

        var placed = GraphLayout.Arrange(items, edges, 0, 0);

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                var a = placed[items[i].Key];
                var b = placed[items[j].Key];
                bool apart =
                    a.X + items[i].Width <= b.X || b.X + items[j].Width <= a.X ||
                    a.Y + items[i].Height <= b.Y || b.Y + items[j].Height <= a.Y;
                Assert.True(apart, "items " + i + " and " + j + " overlap");
            }
        }
    }

    [Fact]
    public void ForeignAndSelfEdges_AreIgnored()
    {
        var items = new[] { Item("a"), Item("b") };
        var edges = new[]
        {
            ((object)"a", (object)"a"),           // self loop
            ((object)"ghost", (object)"b"),       // not in the selection
            ((object)"a", (object)"elsewhere"),   // not in the selection
        };

        var placed = GraphLayout.Arrange(items, edges, 0, 0);

        Assert.Equal(placed["a"].X, placed["b"].X); // no real dependency = one column
    }

    [Fact]
    public void Cycles_Settle_InsteadOfSpinning()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var edges = new[]
        {
            ((object)"a", (object)"b"), ((object)"b", (object)"c"), ((object)"c", (object)"a"),
        };

        var placed = GraphLayout.Arrange(items, edges, 0, 0);

        Assert.Equal(3, placed.Count);
        foreach (var item in items)
        {
            Assert.True(placed.ContainsKey(item.Key));
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void UnusableSizes_NeverProduceNonFinitePositions(double badSize)
    {
        // An auto-sized node reports NaN, and Math.Max(240, NaN) is NaN — the
        // crash that took Navisworks down when Ctrl+L met an un-resized node.
        var items = new[]
        {
            new GraphLayout.LayoutItem("a", badSize, badSize),
            Item("b"),
        };

        var placed = GraphLayout.Arrange(items, new[] { ((object)"a", (object)"b") }, 0, 0);

        Assert.Equal(2, placed.Count);
        foreach (var position in placed.Values)
        {
            Assert.False(double.IsNaN(position.X) || double.IsInfinity(position.X));
            Assert.False(double.IsNaN(position.Y) || double.IsInfinity(position.Y));
        }
    }

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    [InlineData(double.NegativeInfinity, double.PositiveInfinity)]
    public void UnusableAnchor_FallsBackToTheOrigin(double originX, double originY)
    {
        var placed = GraphLayout.Arrange(new[] { Item("a"), Item("b") },
                                         new List<(object, object)>(), originX, originY);

        foreach (var position in placed.Values)
        {
            Assert.False(double.IsNaN(position.X) || double.IsInfinity(position.X));
            Assert.False(double.IsNaN(position.Y) || double.IsInfinity(position.Y));
        }
    }

    [Fact]
    public void UnusableGaps_AreTreatedAsZero_NotPropagated()
    {
        var placed = GraphLayout.Arrange(new[] { Item("a"), Item("b") },
                                         new List<(object, object)>(), 0, 0,
                                         columnGap: double.NaN, rowGap: double.NaN);

        Assert.Equal(0, placed["a"].X);
        Assert.Equal(0, placed["b"].X);
        Assert.False(double.IsNaN(placed["b"].Y));
    }

    [Fact]
    public void EmptySelection_ReturnsNothing()
    {
        Assert.Empty(GraphLayout.Arrange(new GraphLayout.LayoutItem[0], new List<(object, object)>(), 0, 0));
    }
}
