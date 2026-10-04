using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CamelGraph.Core.Editing;
using CamelGraph.Core.Graph;
using Xunit;

namespace CamelGraph.Core.Tests;

public class ArrangeEngineTests
{
    private static GraphLayout.LayoutItem Item(string key, double w = 220, double h = 90) => new GraphLayout.LayoutItem(key, w, h);

    private static List<(object, object)> Edges(params (string, string)[] pairs) =>
        pairs.Select(p => ((object)p.Item1, (object)p.Item2)).ToList();

    private static void AssertNoOverlap(IReadOnlyList<GraphLayout.LayoutItem> items, ArrangeResult result)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var a = result.Positions[items[i].Key];
            for (var j = i + 1; j < items.Count; j++)
            {
                var b = result.Positions[items[j].Key];
                var ox = Math.Min(a.X + items[i].Width, b.X + items[j].Width) - Math.Max(a.X, b.X);
                var oy = Math.Min(a.Y + items[i].Height, b.Y + items[j].Height) - Math.Max(a.Y, b.Y);
                Assert.False(ox > 0.5 && oy > 0.5, "nodes " + items[i].Key + " and " + items[j].Key + " overlap");
            }
        }
    }

    [Fact]
    public void AChainRunsLeftToRightWithTheGapBetweenColumns()
    {
        var items = new[] { Item("a", 300, 40), Item("b", 300, 40), Item("c", 300, 40), Item("d", 300, 40) };
        var result = ArrangeEngine.Arrange(items, Edges(("a", "b"), ("b", "c"), ("c", "d")), 0, 0, columnGap: 80, rowGap: 40);

        Assert.Equal("MSAGL", result.Engine);
        Assert.Null(result.Note);
        var x = items.Select(i => result.Positions[i.Key].X).ToArray();
        Assert.True(x[0] < x[1] && x[1] < x[2] && x[2] < x[3], "columns must increase along the flow: " + string.Join(", ", x));
        Assert.True(x[1] - x[0] >= 300 + 60, "columns must clear the node width plus most of the gap, got " + (x[1] - x[0]));
        AssertNoOverlap(items, result);
    }

    [Fact]
    public void ADiamondHasNoOverlapAndSourceBeforeSink()
    {
        var items = new[] { Item("src"), Item("l", 260, 120), Item("r", 180, 200), Item("sink") };
        var result = ArrangeEngine.Arrange(items, Edges(("src", "l"), ("src", "r"), ("l", "sink"), ("r", "sink")), 100, 200);

        Assert.Equal("MSAGL", result.Engine);
        AssertNoOverlap(items, result);
        Assert.True(result.Positions["src"].X < result.Positions["l"].X);
        Assert.True(result.Positions["l"].X < result.Positions["sink"].X);
        Assert.True(result.Positions["r"].X < result.Positions["sink"].X);
    }

    [Fact]
    public void TheBlockStaysWhereItWasLeftEdgeAndVerticalCentre()
    {
        var items = new[] { Item("a"), Item("b"), Item("c"), Item("d") };
        var result = ArrangeEngine.Arrange(items, Edges(("a", "b"), ("a", "c"), ("b", "d"), ("c", "d")), originX: 500, originY: -300);

        var left = items.Min(i => result.Positions[i.Key].X);
        var top = items.Min(i => result.Positions[i.Key].Y);
        var bottom = items.Max(i => result.Positions[i.Key].Y + i.Height);
        Assert.Equal(500, left, 3);
        Assert.Equal(-300, (top + bottom) / 2d, 3);
    }

    [Fact]
    public void TheSameInputGivesTheSameOutput()
    {
        var items = Enumerable.Range(0, 12).Select(i => Item("n" + i, 200 + i * 10, 80 + i * 5)).ToArray();
        var edges = Edges(("n0", "n1"), ("n0", "n2"), ("n1", "n3"), ("n2", "n3"), ("n3", "n4"), ("n4", "n5"), ("n2", "n6"), ("n6", "n7"), ("n7", "n11"), ("n8", "n9"), ("n9", "n10"));

        var first = ArrangeEngine.Arrange(items, edges, 0, 0);
        var second = ArrangeEngine.Arrange(items, edges, 0, 0);

        Assert.Equal("MSAGL", first.Engine);
        foreach (var item in items)
        {
            Assert.Equal(first.Positions[item.Key], second.Positions[item.Key]);
        }

        AssertNoOverlap(items, first);
    }

    [Fact]
    public void DegenerateSizesNeverProduceNonFiniteCoordinates()
    {
        var items = new[]
        {
            Item("zero", 0, 0), Item("nan", double.NaN, double.NaN), Item("huge", 1e7, 1e7), Item("neg", -5, -5), Item("ok"),
        };
        var result = ArrangeEngine.Arrange(items, Edges(("zero", "nan"), ("nan", "huge"), ("huge", "neg"), ("neg", "ok")), 0, 0);

        Assert.All(items, i =>
        {
            var p = result.Positions[i.Key];
            Assert.False(double.IsNaN(p.X) || double.IsInfinity(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.Y));
        });
    }

    [Fact]
    public void CyclesAndSelfLoopsAndStrayEdgesDoNotThrow()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var result = ArrangeEngine.Arrange(items, Edges(("a", "b"), ("b", "c"), ("c", "a"), ("a", "a"), ("a", "ghost")), 0, 0);
        Assert.Equal(3, result.Positions.Count);
        AssertNoOverlap(items, result);
    }

    [Fact]
    public void TooFewNodesUseTheBuiltInLayout()
    {
        var items = new[] { Item("a"), Item("b") };
        var result = ArrangeEngine.Arrange(items, Edges(("a", "b")), 0, 0);
        Assert.Equal("columns", result.Engine);
        Assert.NotNull(result.Note);
        Assert.True(result.Positions["a"].X < result.Positions["b"].X);
    }

    [Fact]
    public void SwitchingMsaglOffUsesTheBuiltInLayout()
    {
        var items = new[] { Item("a"), Item("b"), Item("c") };
        var result = ArrangeEngine.Arrange(items, Edges(("a", "b"), ("b", "c")), 0, 0, useMsagl: false);
        Assert.Equal("columns", result.Engine);
        Assert.Equal(3, result.Positions.Count);
    }

    [Fact]
    public void ABudgetThatCannotBeMetFallsBackToTheBuiltInLayout()
    {
        var items = Enumerable.Range(0, 250).Select(i => Item("n" + i)).ToArray();
        var pairs = new List<(string, string)>();
        for (var i = 1; i < 250; i++)
        {
            pairs.Add(("n" + (i / 2), "n" + i));
            if (i > 3)
            {
                pairs.Add(("n" + (i - 3), "n" + i));
            }
        }

        var result = ArrangeEngine.Arrange(items, Edges(pairs.ToArray()), 0, 0, budget: TimeSpan.Zero);

        Assert.Equal("columns", result.Engine);
        Assert.Contains("longer than", result.Note);
        Assert.Equal(250, result.Positions.Count);
    }

    [Fact]
    public void ThreeHundredNodesArrangeQuicklyWithoutOverlap()
    {
        var random = new Random(7);
        var items = Enumerable.Range(0, 300).Select(i => Item("n" + i, 180 + random.Next(120), 70 + random.Next(120))).ToArray();
        var pairs = new List<(string, string)>();
        for (var i = 1; i < 300; i++)
        {
            pairs.Add(("n" + random.Next(i), "n" + i));
            if (i > 10 && random.Next(3) == 0)
            {
                pairs.Add(("n" + random.Next(i), "n" + i));
            }
        }

        var timer = Stopwatch.StartNew();
        var result = ArrangeEngine.Arrange(items, Edges(pairs.ToArray()), 0, 0, budget: TimeSpan.FromSeconds(20));
        timer.Stop();

        Assert.Equal("MSAGL", result.Engine);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), "took " + timer.Elapsed);
        AssertNoOverlap(items, result);
    }
}
