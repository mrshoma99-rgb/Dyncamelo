using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace CamelGraph.Nodes.Tests;

public class ListStatsNodesTests
{
    private static IList<object?> L(params object?[] items) => new List<object?>(items);

    [Fact]
    public void Sum_AddsNumbersOfAnyTypeAndNumericText_SkippingNulls()
    {
        Assert.Equal(10.5, ListStatsNodes.Sum(L(1, 2.5, 3L, "4", null)), 9);
    }

    [Fact]
    public void Sum_OfNothingIsZero_ProductOfNothingIsOne()
    {
        Assert.Equal(0d, ListStatsNodes.Sum(L()));
        Assert.Equal(1d, ListStatsNodes.Product(L()));
        Assert.Equal(24d, ListStatsNodes.Product(L(1, 2, 3, 4)));
    }

    [Fact]
    public void Sum_NamesTheItemThatIsNotANumber()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.Sum(L(1, "abc", 3)));
        Assert.Contains("item 1", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    [Fact]
    public void Sum_RefusesNestedLists_AndSaysToFlatten()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.Sum(L(1, L(2, 3))));
        Assert.Contains("Flatten", ex.Message);
    }

    [Fact]
    public void Sum_NeedsAList()
    {
        Assert.Throws<ArgumentNullException>(() => ListStatsNodes.Sum(null!));
    }

    [Fact]
    public void Average_IsTheMean_AndNeedsANumber()
    {
        Assert.Equal(2.5, ListStatsNodes.Average(L(1, 2, 3, 4)));
        Assert.Equal(2d, ListStatsNodes.Average(L(1, null, 3)));
        Assert.Throws<InvalidOperationException>(() => ListStatsNodes.Average(L()));
        Assert.Throws<InvalidOperationException>(() => ListStatsNodes.Average(L(null, null)));
    }

    [Fact]
    public void Median_PicksTheMiddle_OrAveragesTheTwoMiddleOnes()
    {
        Assert.Equal(3d, ListStatsNodes.Median(L(5, 1, 3)));
        Assert.Equal(2.5, ListStatsNodes.Median(L(4, 1, 3, 2)));
        Assert.Equal(7d, ListStatsNodes.Median(L(7)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 2)]
    [InlineData(50, 3)]
    [InlineData(75, 4)]
    [InlineData(100, 5)]
    [InlineData(10, 1.4)]
    public void Percentile_InterpolatesLikeExcel(double percent, double expected)
    {
        Assert.Equal(expected, ListStatsNodes.Percentile(L(5, 3, 1, 4, 2), percent), 9);
    }

    [Fact]
    public void Percentile_RejectsAnOutOfRangePercentage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ListStatsNodes.Percentile(L(1, 2), 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => ListStatsNodes.Percentile(L(1, 2), -1));
    }

    [Fact]
    public void StandardDeviation_PopulationAndSample()
    {
        var data = L(2, 4, 4, 4, 5, 5, 7, 9);
        Assert.Equal(2d, ListStatsNodes.StandardDeviation(data), 9);
        Assert.Equal(2.138089935, ListStatsNodes.StandardDeviation(data, sample: true), 8);
        Assert.Equal(0d, ListStatsNodes.StandardDeviation(L(5)));
        Assert.Throws<InvalidOperationException>(() => ListStatsNodes.StandardDeviation(L(5), sample: true));
    }

    [Fact]
    public void Statistics_GivesEveryFigureAtOnce()
    {
        var stats = ListStatsNodes.Statistics(L(2, 4, 4, 4, 5, 5, 7, 9, null));
        Assert.Equal(8, stats["count"]);
        Assert.Equal(40d, stats["sum"]);
        Assert.Equal(2d, stats["min"]);
        Assert.Equal(9d, stats["max"]);
        Assert.Equal(5d, stats["average"]);
        Assert.Equal(4.5, stats["median"]);
        Assert.Equal(2d, (double)stats["standardDeviation"], 9);
    }

    [Fact]
    public void Statistics_OfNothing_HasCountAndSumButNoSpread()
    {
        var stats = ListStatsNodes.Statistics(L());
        Assert.Equal(0, stats["count"]);
        Assert.Equal(0d, stats["sum"]);
        Assert.Null(stats["min"]);
        Assert.Null(stats["max"]);
        Assert.Null(stats["average"]);
        Assert.Null(stats["median"]);
        Assert.Null(stats["standardDeviation"]);
    }

    [Fact]
    public void CumulativeSum_RunsAlongTheList()
    {
        Assert.Equal(new[] { 1d, 3d, 6d, 10d }, ListStatsNodes.CumulativeSum(L(1, 2, 3, 4)).ToArray());
        Assert.Empty(ListStatsNodes.CumulativeSum(L()));
    }

    [Fact]
    public void CumulativeSum_RefusesNulls_SoPositionsStayAligned()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.CumulativeSum(L(1, null)));
        Assert.Contains("item 1", ex.Message);
    }

    [Fact]
    public void CountBy_TalliesInOrderOfFirstAppearance()
    {
        var result = ListStatsNodes.CountBy(L("b", "a", "b", "c", "b", "a"));
        Assert.Equal(new object?[] { "b", "a", "c" }, (List<object?>)result["values"]);
        Assert.Equal(new object?[] { 3, 2, 1 }, (List<object?>)result["counts"]);
    }

    [Fact]
    public void CountBy_TreatsNumbersOfDifferentTypesAsTheSameValue_AndCountsNulls()
    {
        var result = ListStatsNodes.CountBy(L(1, 1.0, 1L, null, null, 2));
        var values = (List<object?>)result["values"];
        var counts = (List<object?>)result["counts"];
        Assert.Equal(3, values.Count);
        Assert.Equal(3, counts[0]);
        Assert.Null(values[1]);
        Assert.Equal(2, counts[1]);
        Assert.Equal(1, counts[2]);
    }

    [Fact]
    public void Duplicates_ListsOnlyRepeatedValues_NotNulls()
    {
        var result = ListStatsNodes.Duplicates(L("g1", "g2", "g1", null, null, "g3", "g2", "g1"));
        Assert.Equal(new object?[] { "g1", "g2" }, (List<object?>)result["duplicates"]);
        Assert.Equal(new object?[] { 3, 2 }, (List<object?>)result["counts"]);
        Assert.Empty((List<object?>)ListStatsNodes.Duplicates(L(1, 2, 3))["duplicates"]);
    }

    [Fact]
    public void MostCommon_PicksTheModeAndTheEarliestOnATie()
    {
        var result = ListStatsNodes.MostCommon(L("a", "b", "b", "a", "c"));
        Assert.Equal("a", result["item"]);
        Assert.Equal(2, result["count"]);
        Assert.Equal("z", ListStatsNodes.MostCommon(L(null, "z"))["item"]);
        Assert.Throws<InvalidOperationException>(() => ListStatsNodes.MostCommon(L(null, null)));
    }

    [Fact]
    public void Histogram_SplitsTheRangeIntoEqualBins()
    {
        var result = ListStatsNodes.Histogram(L(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10), 5);
        Assert.Equal(new object?[] { 2, 2, 2, 2, 3 }, (List<object?>)result["counts"]);
        Assert.Equal(new object?[] { 0d, 2d, 4d, 6d, 8d }, (List<object?>)result["lower"]);
        Assert.Equal(new object?[] { 2d, 4d, 6d, 8d, 10d }, (List<object?>)result["upper"]);
        Assert.Equal("0 – 2", ((List<object?>)result["labels"])[0]);
    }

    [Fact]
    public void Histogram_OfIdenticalNumbers_PutsThemAllInTheFirstBin()
    {
        var result = ListStatsNodes.Histogram(L(5, 5, 5), 3);
        Assert.Equal(new object?[] { 3, 0, 0 }, (List<object?>)result["counts"]);
    }

    [Fact]
    public void Histogram_ValidatesTheBinCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ListStatsNodes.Histogram(L(1, 2), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ListStatsNodes.Histogram(L(1, 2), 1001));
        Assert.Throws<InvalidOperationException>(() => ListStatsNodes.Histogram(L(), 3));
    }

    [Fact]
    public void FilterByValue_SplitsIntoMatchedRejectedAndMask()
    {
        var result = ListStatsNodes.FilterByValue(L(5, 20, 12, 30), ">", 10);
        Assert.Equal(new object?[] { 20, 12, 30 }, (List<object?>)result["matched"]);
        Assert.Equal(new object?[] { 5 }, (List<object?>)result["rejected"]);
        Assert.Equal(new object?[] { false, true, true, true }, (List<object?>)result["mask"]);
    }

    [Fact]
    public void FilterByValue_TestsAParallelKeyList_WhenGiven()
    {
        var items = L("wallA", "doorB", "wallC");
        var keys = L("Wall", "Door", "Wall");
        var result = ListStatsNodes.FilterByValue(items, "==", "wall", keys);
        Assert.Equal(new object?[] { "wallA", "wallC" }, (List<object?>)result["matched"]);
    }

    [Fact]
    public void FilterByValue_RejectsKeysOfADifferentLength()
    {
        Assert.Throws<ArgumentException>(() => ListStatsNodes.FilterByValue(L(1, 2), "==", 1, L(1)));
    }

    [Theory]
    [InlineData("contains", "all", 2)]
    [InlineData("startsWith", "Wall", 2)]
    [InlineData("endsWith", "01", 2)]
    [InlineData("matches", "Wall*", 2)]
    [InlineData("matches", "?all-01", 1)]
    [InlineData("regex", "^(Wall|Door)-0\\d$", 3)]
    [InlineData("!contains", "Wall", 1)]
    public void FilterByValue_TextTests(string test, string value, int expectedMatches)
    {
        var items = L("Wall-01", "Wall-02", "Door-01");
        var result = ListStatsNodes.FilterByValue(items, test, value);
        Assert.Equal(expectedMatches, ((List<object?>)result["matched"]).Count);
    }

    [Fact]
    public void FilterByValue_TextTestsRespectIgnoreCase()
    {
        var items = L("Wall", "wall", "WALL");
        Assert.Equal(3, ((List<object?>)ListStatsNodes.FilterByValue(items, "==", "wall")["matched"]).Count);
        Assert.Single((List<object?>)ListStatsNodes.FilterByValue(items, "==", "wall", null, false)["matched"]);
    }

    [Fact]
    public void FilterByValue_NullAndEmptyTests()
    {
        var items = L("a", null, "", L());
        Assert.Single((List<object?>)ListStatsNodes.FilterByValue(items, "isNull")["matched"]);
        Assert.Equal(3, ((List<object?>)ListStatsNodes.FilterByValue(items, "isEmpty")["matched"]).Count);
        Assert.Single((List<object?>)ListStatsNodes.FilterByValue(items, "notEmpty")["matched"]);
    }

    [Fact]
    public void FilterByValue_ReadsNumericTextAsNumbers()
    {
        var result = ListStatsNodes.FilterByValue(L("5", "20", "100"), ">=", 20);
        Assert.Equal(new object?[] { "20", "100" }, (List<object?>)result["matched"]);
    }

    [Fact]
    public void FilterByValue_NamesAnUnknownTest()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.FilterByValue(L(1), "approximately", 1));
        Assert.Contains("approximately", ex.Message);
        Assert.Contains("contains", ex.Message);
    }

    [Fact]
    public void FilterByValue_ExplainsABadRegex()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.FilterByValue(L("a"), "regex", "("));
        Assert.Contains("regular expression", ex.Message);
    }

    [Fact]
    public void WithIndex_PairsPositionAndItem()
    {
        var pairs = ListStatsNodes.WithIndex(L("a", "b"));
        Assert.Equal(2, pairs.Count);
        Assert.Equal(new object?[] { 1, "b" }, (List<object?>)pairs[1]!);
    }

    [Fact]
    public void Zip_PairsToTheShorterList()
    {
        var pairs = ListStatsNodes.Zip(L(1, 2, 3), L("a", "b"));
        Assert.Equal(2, pairs.Count);
        Assert.Equal(new object?[] { 2, "b" }, (List<object?>)pairs[1]!);
        Assert.Throws<ArgumentNullException>(() => ListStatsNodes.Zip(L(1), null!));
    }

    [Fact]
    public void Pairs_SlidesAWindowOfTwo_OptionallyClosingTheLoop()
    {
        var open = ListStatsNodes.Pairs(L(1, 2, 3));
        Assert.Equal(2, open.Count);
        Assert.Equal(new object?[] { 2, 3 }, (List<object?>)open[1]!);

        var closed = ListStatsNodes.Pairs(L(1, 2, 3), cyclic: true);
        Assert.Equal(3, closed.Count);
        Assert.Equal(new object?[] { 3, 1 }, (List<object?>)closed[2]!);

        Assert.Empty(ListStatsNodes.Pairs(L(1)));
        Assert.Empty(ListStatsNodes.Pairs(L(), cyclic: true));
    }

    [Fact]
    public void SortDescending_PutsTheLargestFirst()
    {
        Assert.Equal(new object?[] { 9, 5, 1 }, ListStatsNodes.SortDescending(L(5, 9, 1)).ToArray());
        Assert.Equal(new object?[] { "c", "b", "a" }, ListStatsNodes.SortDescending(L("b", "c", "a")).ToArray());
    }

    [Fact]
    public void SortDescending_ExplainsIncomparableTypes()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ListStatsNodes.SortDescending(L(1, "a")));
        Assert.Contains("List.SortDescending", ex.Message);
    }

    [Fact]
    public void Shuffle_IsRepeatableForASeed_AndKeepsEveryItem()
    {
        var list = L(Enumerable.Range(0, 20).Cast<object?>().ToArray());
        var first = ListStatsNodes.Shuffle(list, 7);
        var second = ListStatsNodes.Shuffle(list, 7);
        Assert.Equal(first, second);
        Assert.NotEqual(list, first);
        Assert.Equal(list.OrderBy(x => (int)x!), first.OrderBy(x => (int)x!));
        Assert.NotEqual(first, ListStatsNodes.Shuffle(list, 8));
    }

    [Fact]
    public void TakeWhileAndDropWhile_SplitAtTheFirstFalse()
    {
        var items = L("a", "b", "c", "d");
        var mask = L(true, true, false, true);
        Assert.Equal(new object?[] { "a", "b" }, ListStatsNodes.TakeWhile(items, mask).ToArray());
        Assert.Equal(new object?[] { "c", "d" }, ListStatsNodes.DropWhile(items, mask).ToArray());
        Assert.Equal(4, ListStatsNodes.TakeWhile(items, L(true, true, true, true)).Count);
        Assert.Empty(ListStatsNodes.DropWhile(items, L(true, true, true, true)));
    }

    [Fact]
    public void TakeWhile_NeedsAMaskOfTheSameLength()
    {
        Assert.Throws<ArgumentException>(() => ListStatsNodes.TakeWhile(L(1, 2), L(true)));
        Assert.Throws<ArgumentNullException>(() => ListStatsNodes.TakeWhile(L(1, 2), null!));
    }
}
