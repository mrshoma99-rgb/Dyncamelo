using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;
using static CamelGraph.Nodes.Tests.NodeRun;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// The aggregate and ordering rules of the list nodes: blank cells in a column of numbers, an empty list, one ordering rule, the
/// mask nodes, the size caps and the friendly messages (COL-02, COL-08, COL-12, COL-14, COL-23, ENG-09, ENG-12, ENG-15, ENG-16).
/// </summary>
public class ListAggregateAndOrderTests
{
    private static string Messages(ZeroTouchNodeModel node) => string.Join(" | ", node.Messages.Select(m => m.Text));

    // ------------------------------------------------------------ COL-02 blank cells

    [Fact]
    public void BlankTextCellsAreSkippedLikeNulls_ByEveryNumericNode()
    {
        var column = L(1, "", 3, "   ", null, "2.5");

        Assert.Equal(6.5, ListStatsNodes.Sum(column));
        Assert.Equal(7.5, ListStatsNodes.Product(column));
        Assert.Equal(6.5 / 3, ListStatsNodes.Average(column)!.Value, 9);
        Assert.Equal(2.5, ListStatsNodes.Median(column));
        Assert.Equal(3d, ListStatsNodes.Percentile(column, 100d));
        Assert.True(ListStatsNodes.StandardDeviation(column) > 0);
        Assert.Equal(3, ListStatsNodes.Statistics(column)["count"]);
        Assert.Equal(3, ((List<object?>)ListStatsNodes.Histogram(column, 3)["counts"]).Sum(c => (int)c!));
    }

    [Fact]
    public void CumulativeSum_NamesABlankCellTheWayItNamesANull()
    {
        var blank = Assert.Throws<ArgumentException>(() => ListStatsNodes.CumulativeSum(L(1, "", 3)));
        Assert.Contains("item 1", blank.Message);
        Assert.Contains("empty", blank.Message);
        var spaces = Assert.Throws<ArgumentException>(() => ListStatsNodes.CumulativeSum(L(1, 2, "  ")));
        Assert.Contains("item 2", spaces.Message);
    }

    [Fact]
    public void ASumOverTableColumnWithBlanksRunsUnderTheEngine()
    {
        var node = Run("List.Sum", L(1, "", 2));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(3d, node.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ COL-23 nested-list message

    [Fact]
    public void ANestedListInTheStatisticsNodes_PointsToTheListLevelAsWellAsFlatten()
    {
        foreach (var run in new Action[]
        {
            () => ListStatsNodes.Sum(L(1, L(2))),
            () => ListStatsNodes.Average(L(1, L(2))),
            () => ListStatsNodes.Median(L(1, L(2))),
            () => ListStatsNodes.Percentile(L(1, L(2)), 50),
            () => ListStatsNodes.StandardDeviation(L(1, L(2))),
            () => ListStatsNodes.Histogram(L(1, L(2)), 3),
            () => ListStatsNodes.Statistics(L(1, L(2))),
            () => ListStatsNodes.CumulativeSum(L(1, L(2))),
        })
        {
            var ex = Assert.Throws<ArgumentException>(run);
            Assert.Contains("@L2", ex.Message);
            Assert.Contains("List.Flatten", ex.Message);
        }
    }

    // ------------------------------------------------------------ COL-14 / ENG-12 empty list

    [Theory]
    [InlineData("List.Average")]
    [InlineData("List.Median")]
    [InlineData("List.Percentile")]
    [InlineData("List.StandardDeviation")]
    public void AnAggregateOfNoNumbers_IsAnEmptyResultAndAWarning_NotAnError(string name)
    {
        var node = Run(name, L());

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Null(node.OutPorts[0].Value);
        Assert.Contains("'list' input has no numbers", Messages(node));
    }

    [Fact]
    public void ListsOfNullsAndBlanksCountAsNoNumbers()
    {
        var node = Run("List.Average", L(null, "", " "));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Null(node.OutPorts[0].Value);
    }

    [Fact]
    public void StatisticsOfNothing_KeepsCountAndSum_AndWarns()
    {
        var node = Run("List.Statistics", L());

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(0, node.OutPorts[0].Value);
        Assert.Equal(0d, node.OutPorts[1].Value);
        Assert.All(node.OutPorts.Skip(2), p => Assert.Null(p.Value));
        Assert.Contains("minimum, maximum, average, median and standard deviation are empty", Messages(node));
    }

    [Fact]
    public void HistogramOfNothing_GivesFourEmptyLists_AndWarns()
    {
        var node = Run("List.Histogram", L(), 4);

        Assert.Equal(NodeState.Warning, node.State);
        Assert.All(node.OutPorts, p => Assert.Empty(Assert.IsAssignableFrom<IList<object?>>(p.Value)));
    }

    [Fact]
    public void SampleStandardDeviationNeedsTwoNumbers_AndSaysSo()
    {
        var node = Run("List.StandardDeviation", L(5), true);

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Null(node.OutPorts[0].Value);
        Assert.Contains("at least two numbers", Messages(node));
    }

    [Fact]
    public void SumAndProductKeepTheirIdentityForAnEmptyList_WithoutAWarning()
    {
        var sum = Run("List.Sum", L());
        var product = Run("List.Product", L());

        Assert.Equal(NodeState.Executed, sum.State);
        Assert.Equal(0d, sum.OutPorts[0].Value);
        Assert.Equal(NodeState.Executed, product.State);
        Assert.Equal(1d, product.OutPorts[0].Value);
    }

    [Fact]
    public void MaximumAndMinimumOfNothing_AreEmptyWithAWarning()
    {
        foreach (var name in new[] { "List.MaximumItem", "List.MinimumItem" })
        {
            var node = Run(name, L(null, ""));

            Assert.Equal(NodeState.Warning, node.State);
            Assert.Null(node.OutPorts[0].Value);
            Assert.Contains("nothing to compare", Messages(node));
        }
    }

    [Fact]
    public void FirstLastAndRestOfAnEmptyList_StayErrorsThatSayWhy()
    {
        foreach (var name in new[] { "List.FirstItem", "List.LastItem", "List.RestOfItems" })
        {
            var node = Run(name, L());

            Assert.Equal(NodeState.Error, node.State);
            Assert.Contains("the list is empty", Messages(node));
        }
    }

    [Fact]
    public void Percentile_RefusesNaN_WithASentence()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ListStatsNodes.Percentile(L(1, 2), double.NaN));
        Assert.Contains("0 to 100", ex.Message);
        Assert.Contains("NaN", ex.Message);
        Assert.DoesNotContain("Index was out of range", ex.Message);
    }

    // ------------------------------------------------------------ COL-18 multi input

    [Theory]
    [InlineData("List.Sum")]
    [InlineData("List.Product")]
    [InlineData("List.Average")]
    [InlineData("List.Median")]
    [InlineData("List.Percentile")]
    [InlineData("List.StandardDeviation")]
    [InlineData("List.Histogram")]
    [InlineData("List.Statistics")]
    [InlineData("List.MinimumItem")]
    [InlineData("List.MaximumItem")]
    [InlineData("List.AllTrue")]
    [InlineData("List.AnyTrue")]
    [InlineData("List.CountTrue")]
    public void TheAggregatesTakeSeveralWires(string name)
    {
        Assert.True(Definition(name).Inputs[0].MultiInput, name + " should have a multi-input list");
    }

    [Fact]
    public void TwoWiresIntoProductAreCombined()
    {
        var graph = new GraphModel();
        var a = new FixedValueNode(L(2, 3));
        var b = new FixedValueNode(L(4));
        var node = new ZeroTouchNodeModel(Definition("List.Product"));
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddNode(node);
        Assert.True(graph.Connect(a.OutPorts[0], node.InPorts[0]).Success);
        Assert.True(graph.Connect(b.OutPorts[0], node.InPorts[0]).Success);

        new CamelGraph.Core.Execution.GraphEngine().Run(graph);

        Assert.Equal(24d, node.OutPorts[0].Value);
    }

    // ------------------------------------------------------------ COL-12 one ordering rule

    [Fact]
    public void Sort_TextIgnoresCase_NumbersGoByValue_BlanksGoLastInBothDirections()
    {
        var items = L("pear", "Apple", null, "banana", "", "Cherry");

        Assert.Equal(new object?[] { "Apple", "banana", "Cherry", "pear", null, "" }, ListNodes.Sort(items).ToArray());
        Assert.Equal(new object?[] { "pear", "Cherry", "banana", "Apple", null, "" }, ListNodes.Sort(items, descending: true).ToArray());
        Assert.Equal(new object?[] { 2, 10, "x" }, ListNodes.Sort(L(10, "x", 2)).ToArray());
        Assert.Equal(new object?[] { 9, "10" }, ListNodes.Sort(L("10", 9)).ToArray());          // text that reads as a number is a number against a number
    }

    [Fact]
    public void Sort_IsStable_AndDatesSortAsDates()
    {
        var a = new DateTime(2024, 5, 1);
        var b = new DateTime(2023, 1, 1);

        Assert.Equal(new object?[] { "a", "A", "b" }, ListNodes.Sort(L("b", "a", "A")).ToArray());      // "a" and "A" are equal: input order kept
        Assert.Equal(new object?[] { b, a }, ListNodes.Sort(L(a, b)).ToArray());
    }

    [Fact]
    public void SortByKey_HasADirection_AndTheSameRuleForKeys()
    {
        var ascending = ListNodes.SortByKey(L("x", "y", "z", "w"), L(2, 1, "", 3));
        Assert.Equal(new object?[] { "y", "x", "w", "z" }, (List<object?>)ascending["sorted"]);

        var descending = ListNodes.SortByKey(L("x", "y", "z", "w"), L(2, 1, "", 3), descending: true);
        Assert.Equal(new object?[] { "w", "x", "y", "z" }, (List<object?>)descending["sorted"]);
        Assert.Equal(new object?[] { 3, 2, 1, "" }, (List<object?>)descending["sortedKeys"]);
    }

    [Fact]
    public void SortOrderRunsUnderTheEngine_AndTheDescendingToggleWorks()
    {
        var up = Run("List.Sort", L(3, 1, 2));
        var down = Run("List.Sort", L(3, 1, 2), true);

        Assert.Equal(new object?[] { 1, 2, 3 }, Out(up).ToArray());
        Assert.Equal(new object?[] { 3, 2, 1 }, Out(down).ToArray());
    }

    [Fact]
    public void MaximumAndMinimum_UseTheSameRule()
    {
        Assert.Equal("Zebra", ListNodes.MaximumItem(L("apple", "Zebra", "mango")));
        Assert.Equal("apple", ListNodes.MinimumItem(L("apple", "Zebra", "mango")));
        Assert.Equal("9", ListNodes.MaximumItem(L("10", "9")));                // both text: compared as text
        Assert.Equal(10, ListNodes.MaximumItem(L(10, "9")));                   // a number against numeric text: by value
        Assert.Equal(3, ListNodes.MaximumItem(L("", 3, null)));                // empty items are left out
    }

    [Fact]
    public void MaximumOfListsSaysWhatToDo_NotTheTypeName()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListNodes.MaximumItem(L(L(1), L(2))));
        Assert.Contains("List.MaximumItem", ex.Message);
        Assert.Contains("@L2", ex.Message);
        Assert.DoesNotContain("List`1", ex.Message);
    }

    [Fact]
    public void SortDescending_IsRetired_ButStillRunsLikeSortWithDescending()
    {
        var definition = Definition("List.SortDescending");
        Assert.True(definition.IsDeprecated);
        Assert.Contains("List.Sort", definition.Replacement);

        var node = Run("List.SortDescending", L("b", "C", "a"));
        Assert.Equal(new object?[] { "C", "b", "a" }, Out(node).ToArray());
    }

    [Fact]
    public void AGraphSavedWithTheOldSortIds_StillLoadsAndRuns()
    {
        var sort = RunSavedAs(
            "List.Sort",
            "CamelGraph.Nodes.ListNodes.Sort@System.Collections.Generic.IList<object>",
            new (string, string)[0],
            new (int, object?)[0],
            new[] { (0, (object?)L(3, 1, 2)) });
        Assert.Equal(NodeState.Executed, sort.State);
        Assert.Equal(new object?[] { 1, 2, 3 }, Out(sort).ToArray());

        var byKey = RunSavedAs(
            "List.SortByKey",
            "CamelGraph.Nodes.ListNodes.SortByKey@System.Collections.Generic.IList<object>,System.Collections.Generic.IList<object>",
            new (string, string)[0],
            new (int, object?)[0],
            new[] { (0, (object?)L("a", "b")), (1, (object?)L(2, 1)) });
        Assert.Equal(NodeState.Executed, byKey.State);
        Assert.Equal(new object?[] { "b", "a" }, Out(byKey).ToArray());
    }

    // ------------------------------------------------------------ COL-08 / ENG-09 masks

    [Fact]
    public void AllTrueAnyTrueCountTrue_CountOnlyBooleans_AndWarnAboutNumbers()
    {
        // OLD: a numeric column gave true, true, 3.
        Assert.False(ListNodes.AllTrue(L(3, 1, 2)));
        Assert.False(ListNodes.AnyTrue(L(3, 1, 2)));
        var counts = ListNodes.CountTrue(L(3, 1, 2, true));
        Assert.Equal(1, counts["trueCount"]);
        Assert.Equal(3, counts["falseCount"]);

        Assert.True(ListNodes.AllTrue(L(true, "true", "TRUE")));
        Assert.True(ListNodes.AnyTrue(L(null, false, "True")));
    }

    [Fact]
    public void ANumericMaskIntoAnyTrue_WarnsOnTheNode()
    {
        var node = Run("List.AnyTrue", L("abc", 0, null, true));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(true, node.OutPorts[0].Value);
        Assert.Contains("2 of 4 items are not true or false", Messages(node));
        Assert.Contains("item 0, 'abc'", Messages(node));
    }

    [Fact]
    public void ACleanMaskHasNoWarning()
    {
        var node = Run("List.CountTrue", L(true, null, false, true));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(2, node.OutPorts[0].Value);
        Assert.Equal(2, node.OutPorts[1].Value);
    }

    [Fact]
    public void AListInsideAMask_IsAnErrorThatPointsToTheListLevel()
    {
        foreach (var run in new Action[]
        {
            () => ListNodes.AllTrue(L(true, L(true))),
            () => ListNodes.AnyTrue(L(true, L(true))),
            () => ListNodes.CountTrue(L(true, L(true))),
            () => ListNodes.FilterByBoolMask(L(1, 2), L(true, L(true))),
            () => ListStatsNodes.TakeWhile(L(1, 2), L(true, L(true))),
            () => ListStatsNodes.DropWhile(L(1, 2), L(true, L(true))),
        })
        {
            var ex = Assert.Throws<ArgumentException>(run);
            Assert.Contains("item 1", ex.Message);
            Assert.Contains("@L2", ex.Message);
        }
    }

    [Fact]
    public void TakeWhileAndDropWhile_ReadTheMaskLikeFilterByBoolMask()
    {
        // A null is false and a number is read like FilterByBoolMask reads it (0 false, anything else true).
        Assert.Equal(new object?[] { "a", "b" }, ListStatsNodes.TakeWhile(L("a", "b", "c", "d"), L(true, 1, null, true)).ToArray());
        Assert.Equal(new object?[] { "c", "d" }, ListStatsNodes.DropWhile(L("a", "b", "c", "d"), L(true, 1, null, true)).ToArray());

        var ex = Assert.Throws<ArgumentException>(() => ListStatsNodes.TakeWhile(L("a"), L("maybe")));
        Assert.Contains("not true or false", ex.Message);
        Assert.Contains("'maybe'", ex.Message);
    }

    // ------------------------------------------------------------ COL-13 zip

    [Fact]
    public void Zip_OfUnequalLists_StillPairsToTheShorter_ButWarnsHowManyWereLeftOut()
    {
        var node = Run("List.Zip", L("a", "b", "c", "d", "e"), L(1, 2, 3));

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(3, Out(node).Count);
        Assert.Contains("'first' list has 5 items and the 'second' has 3", Messages(node));
        Assert.Contains("last 2 item(s) of the 'first' list are left out", Messages(node));

        var equal = Run("List.Zip", L("a"), L(1));
        Assert.Equal(NodeState.Executed, equal.State);
    }

    // ------------------------------------------------------------ ENG-15 / ENG-16 messages

    [Fact]
    public void TakeAndDropItems_SurviveTheSmallestInteger()
    {
        Assert.Equal(new object?[] { 1, 2 }, ListNodes.TakeItems(L(1, 2), int.MinValue).ToArray());    // a negative amount counts from the end: all of it
        Assert.Empty(ListNodes.DropItems(L(1, 2), int.MinValue));
        Assert.Equal(new object?[] { 1, 2 }, ListNodes.TakeItems(L(1, 2), int.MaxValue).ToArray());
    }

    [Fact]
    public void TakeEveryNthItem_SurvivesAHugeOffset()
    {
        Assert.Empty(ListNodes.TakeEveryNthItem(L(1, 2, 3), 2, int.MaxValue));
        Assert.Equal(new object?[] { 2 }, ListNodes.TakeEveryNthItem(L(1, 2, 3), 2).ToArray());
        Assert.Equal(new object?[] { 3 }, ListNodes.TakeEveryNthItem(L(1, 2, 3, 4), 2, 1).ToArray());
    }

    [Fact]
    public void Chop_NamesTheBadLength()
    {
        var text = Assert.Throws<ArgumentException>(() => ListNodes.Chop(L(1, 2, 3), L("a")));
        Assert.Contains("length 1 ('a') is not a whole number", text.Message);

        var empty = Assert.Throws<ArgumentException>(() => ListNodes.Chop(L(1, 2, 3), L(2, null)));
        Assert.Contains("length 2 (empty)", empty.Message);

        var nested = Assert.Throws<ArgumentException>(() => ListNodes.Chop(L(1, 2, 3), L(L(3, 1), L(2))));
        Assert.Contains("length 1 is a list", nested.Message);
        Assert.DoesNotContain("IConvertible", nested.Message);

        var zero = Assert.Throws<ArgumentException>(() => ListNodes.Chop(L(1, 2, 3), L(0)));
        Assert.Contains("at least 1", zero.Message);

        Assert.Equal(2, ListNodes.Chop(L(1, 2, 3), L(2)).Count);
    }

    [Fact]
    public void DuplicatesAndMostCommon_NameThemselvesWhenTheListIsMissing()
    {
        var duplicates = Assert.Throws<ArgumentNullException>(() => ListStatsNodes.Duplicates(null!));
        Assert.Contains("List.Duplicates", duplicates.Message);
        Assert.DoesNotContain("List.CountValues", duplicates.Message);
        var common = Assert.Throws<ArgumentNullException>(() => ListStatsNodes.MostCommon(null!));
        Assert.Contains("List.MostCommon", common.Message);
    }

    // ------------------------------------------------------------ COL-30 names

    [Fact]
    public void CountByIsNowCalledCountValues_AndStillFoundByItsOldName()
    {
        var names = NodeRegistry().Select(d => d.Name).ToList();
        Assert.Contains("List.CountValues", names);
        Assert.DoesNotContain("List.CountBy", names);
        Assert.Contains("countby", Definition("List.CountValues").SearchTags);
        Assert.Equal(
            "CamelGraph.Nodes.ListStatsNodes.CountBy@System.Collections.Generic.IList<object>",
            Definition("List.CountValues").Id);
    }

    [Fact]
    public void ReverseNowNamesItsOutputList_AndAGraphSavedWithTheOldNameStillLoads()
    {
        Assert.Equal("list", Definition("List.Reverse").Outputs[0].Name);

        var reverse = RunSavedAs(
            "List.Reverse",
            Definition("List.Reverse").Id,
            new (string, string)[0],
            new (int, object?)[0],
            new[] { (0, (object?)L(1, 2, 3)) });
        Assert.Equal(new object?[] { 3, 2, 1 }, Out(reverse).ToArray());
    }

    private static IEnumerable<CamelGraph.Core.Loader.NodeDefinition> NodeRegistry() => Registry.Definitions;
}
