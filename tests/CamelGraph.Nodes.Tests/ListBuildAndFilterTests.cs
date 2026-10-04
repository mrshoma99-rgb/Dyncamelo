using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;
using Xunit;
using static CamelGraph.Nodes.Tests.NodeRun;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// Lists built from a number (size caps, no drift), several indices at once, slicing to the end, and List.FilterByValue
/// (ordering tests and blanks, membership, friendly regex failure): COL-03, COL-07, COL-16, COL-19, COL-35, ENG-13, ENG-15.
/// </summary>
public class ListBuildAndFilterTests
{
    private static string Messages(ZeroTouchNodeModel node) => string.Join(" | ", node.Messages.Select(m => m.Text));

    // ------------------------------------------------------------ COL-07 / ENG-13 caps and drift

    [Fact]
    public void Range_DoesNotDrift_AndKeepsTheInclusiveEnd()
    {
        var tenths = ListNodes.Range(0, 1, 0.1);
        Assert.Equal(11, tenths.Count);
        Assert.Equal(0.8, tenths[8]);          // OLD: 0.7999999999999999 (0.1 added eight times)
        Assert.Equal(0.9, tenths[9]);          // OLD: 0.8999999999999999
        Assert.Equal(1.0, tenths[10]);

        Assert.Equal(new[] { 5d, 3d, 1d }, ListNodes.Range(5, 1, -2).ToArray());
        Assert.Equal(new[] { 1d, 2d, 3d }, ListNodes.Range(1, 3).ToArray());
        Assert.Empty(ListNodes.Range(5, 1, 1));                // a step that moves away from end
        Assert.Equal(new[] { 2d }, ListNodes.Range(2, 2, 1).ToArray());
    }

    [Fact]
    public void Range_RefusesAHugeSequence_BeforeBuildingIt()
    {
        var tooMany = Assert.Throws<ArgumentException>(() => ListNodes.Range(0, 5, 1e-9));
        Assert.Contains("1,000,000", tooMany.Message);
        Assert.Contains("larger step", tooMany.Message);

        Assert.Equal(1000000, ListNodes.Range(1, 1000000).Count);          // exactly the limit is fine
        Assert.Throws<ArgumentException>(() => ListNodes.Range(1, 1000001));
    }

    [Fact]
    public void Range_RefusesNumbersThatAreNotFinite()
    {
        var infinite = Assert.Throws<ArgumentException>(() => ListNodes.Range(1, double.PositiveInfinity));
        Assert.Contains("finite", infinite.Message);
        Assert.Throws<ArgumentException>(() => ListNodes.Range(double.NaN, 1));
        Assert.Throws<ArgumentException>(() => ListNodes.Range(0, 1, double.NaN));
        Assert.Throws<ArgumentException>(() => ListNodes.Range(0, 1, 0));
    }

    [Fact]
    public void OfRepeatedItemAndCycle_AreCapped_AndAnEmptyListCycleIsInstant()
    {
        var repeated = Assert.Throws<ArgumentException>(() => ListNodes.OfRepeatedItem("abc", int.MaxValue));
        Assert.Contains("1,000,000", repeated.Message);
        Assert.Equal(1000000, ListNodes.OfRepeatedItem("x", 1000000).Count);

        var cycled = Assert.Throws<ArgumentException>(() => ListNodes.Cycle(L(3, 1, 2), int.MaxValue));
        Assert.Contains("1,000,000", cycled.Message);
        Assert.Equal(6, ListNodes.Cycle(L("a", "b"), 3).Count);

        // Nothing to repeat is nothing, however many times (it used to loop two billion times).
        Assert.Empty(ListNodes.Cycle(L(), int.MaxValue));
    }

    [Fact]
    public void ABigRangeIsAnErrorOnTheNode_NotAFailedRun()
    {
        var node = Run("List.Range", 0, 5, 1e-9);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("limit is 1,000,000", Messages(node));
    }

    [Fact]
    public void TheSizeInputsHaveARange()
    {
        Assert.Equal(1000000, Definition("List.OfRepeatedItem").Inputs.Single(i => i.Name == "amount").Range!.Max);
        Assert.Equal(1000000, Definition("List.Cycle").Inputs.Single(i => i.Name == "amount").Range!.Max);
        Assert.Equal(1, Definition("List.Slice").Inputs.Single(i => i.Name == "step").Range!.Min);
        Assert.Equal(1, Definition("List.TakeEveryNthItem").Inputs.Single(i => i.Name == "n").Range!.Min);
        Assert.Equal(0, Definition("List.TakeEveryNthItem").Inputs.Single(i => i.Name == "offset").Range!.Min);
        Assert.Equal(-1, Definition("List.Flatten").Inputs.Single(i => i.Name == "amount").Range!.Min);
    }

    // ------------------------------------------------------------ COL-16 several indices

    [Fact]
    public void RemoveItemAtIndex_RemovesSeveralIndicesInOneList()
    {
        var input = L("a", "b", "c", "d", "e");

        Assert.Equal(new object?[] { "a", "c", "e" }, ListNodes.RemoveItemAtIndex(input, new[] { 1, 3 }).ToArray());
        Assert.Equal(new object?[] { "b", "c", "d" }, ListNodes.RemoveItemAtIndex(input, new[] { 0, -1 }).ToArray());
        Assert.Equal(new object?[] { "a", "b", "c", "d", "e" }, ListNodes.RemoveItemAtIndex(input, new int[0]).ToArray());
        Assert.Equal(new object?[] { "a", "c", "d", "e" }, ListNodes.RemoveItemAtIndex(input, new[] { 1, 1 }).ToArray());      // twice = once
        Assert.Equal(5, input.Count);
    }

    [Fact]
    public void RemoveItemAtIndex_UnderTheEngine_AListOfIndicesMakesOneListAndANumberStillWorks()
    {
        var several = Run("List.RemoveItemAtIndex", L("a", "b", "c", "d"), L(0, 2));
        Assert.Equal(new object?[] { "b", "d" }, Out(several).ToArray());

        var one = Run("List.RemoveItemAtIndex", L("a", "b", "c"), 1);
        Assert.Equal(new object?[] { "a", "c" }, Out(one).ToArray());

        // The output of List.AllIndicesOf feeds it directly: remove every "x".
        var matches = ListNodes.AllIndicesOf(L("x", "a", "x", "b"), "x");
        var cleaned = Run("List.RemoveItemAtIndex", L("x", "a", "x", "b"), matches.Cast<object?>().ToList());
        Assert.Equal(new object?[] { "a", "b" }, Out(cleaned).ToArray());
    }

    [Fact]
    public void RemoveItemAtIndex_OutOfRange_NamesTheIndex()
    {
        var node = Run("List.RemoveItemAtIndex", L("a"), 5);

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("Index 5 is out of range for a list of 1 element(s)", Messages(node));
    }

    [Fact]
    public void AGraphSavedWithTheOldSingleIndexInput_StillLoadsWithItsTypedValueAndRuns()
    {
        // 0.50 and earlier: RemoveItemAtIndex(list, int index), the index typed into the node.
        var node = RunSavedAs(
            "List.RemoveItemAtIndex",
            "CamelGraph.Nodes.ListNodes.RemoveItemAtIndex@System.Collections.Generic.IList<object>,int",
            new[] { ("indices", "index") },
            new[] { (1, (object?)1) },
            new[] { (0, (object?)L("a", "b", "c")) });

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new object?[] { "a", "c" }, Out(node).ToArray());
    }

    // ------------------------------------------------------------ COL-19 slice

    [Fact]
    public void Slice_LeftWithoutAnEnd_GoesToTheEndOfTheList()
    {
        Assert.Equal(new object?[] { 3, 4, 5 }, ListNodes.Slice(L(1, 2, 3, 4, 5), 2).ToArray());
        Assert.Equal(new object?[] { 3, 4 }, ListNodes.Slice(L(1, 2, 3, 4, 5), 2, 4).ToArray());
        Assert.Equal(new object?[] { 1, 4, 7 }, ListNodes.Slice(L(1, 2, 3, 4, 5, 6, 7, 8), 0, null, 3).ToArray());      // thinning: the 1st, 4th, 7th
        Assert.Equal(new object?[] { 4, 5 }, ListNodes.Slice(L(1, 2, 3, 4, 5), -2).ToArray());
    }

    [Fact]
    public void Slice_UnderTheEngine_NeedsNoEndWired()
    {
        var node = Run("List.Slice", L(1, 2, 3, 4), 1);

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new object?[] { 2, 3, 4 }, Out(node).ToArray());
    }

    [Fact]
    public void AGraphSavedWithTheOldSliceSignature_StillLoadsWithItsEndAndRuns()
    {
        var node = RunSavedAs(
            "List.Slice",
            "CamelGraph.Nodes.ListNodes.Slice@System.Collections.Generic.IList<object>,int,int,int",
            new (string, string)[0],
            new[] { (1, (object?)1), (2, (object?)3) },
            new[] { (0, (object?)L(1, 2, 3, 4, 5)) });

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new object?[] { 2, 3 }, Out(node).ToArray());
    }

    // ------------------------------------------------------------ COL-03 FilterByValue ordering

    private static Dictionary<string, object> Filter(IList<object?> list, string test, object? value, IList<object?>? keys = null, bool ignoreCase = true) =>
        ListStatsNodes.FilterByValue(list, test, value, keys, ignoreCase);

    [Fact]
    public void LessThanAndLessOrEqual_DoNotPassAnItemWithNoValue()
    {
        var lengths = L(100, null, 2000, "", 5000, "  ");

        // OLD: < and <= kept every item without a length (null sorted before everything), > and >= dropped them.
        Assert.Equal(new object?[] { 100, 2000 }, (List<object?>)Filter(lengths, "<", 3000)["matched"]);
        Assert.Equal(new object?[] { 100, 2000 }, (List<object?>)Filter(lengths, "<=", 2000)["matched"]);
        Assert.Equal(new object?[] { 5000 }, (List<object?>)Filter(lengths, ">", 3000)["matched"]);
        Assert.Equal(new object?[] { 2000, 5000 }, (List<object?>)Filter(lengths, ">=", 2000)["matched"]);
    }

    [Fact]
    public void IsEmptyAndIsNull_StillSelectTheItemsWithNoValue()
    {
        var result = Filter(L(1, null, ""), "isEmpty", null);

        Assert.Equal(new object?[] { null, "" }, (List<object?>)result["matched"]);
        Assert.Equal(new object?[] { null }, (List<object?>)Filter(L(1, null, ""), "isNull", null)["matched"]);
    }

    [Fact]
    public void ABlankTestValueNeverPassesAnOrderingTest()
    {
        Assert.Empty((List<object?>)Filter(L(1, 2), ">", null)["matched"]);
        Assert.Empty((List<object?>)Filter(L(1, 2), "<", "")["matched"]);
    }

    [Fact]
    public void ATextThatIsNotANumber_DoesNotPassAnOrderingTest_AndTheNodeWarnsInsteadOfFailing()
    {
        var node = Run("List.FilterByValue", L(5, "abc", 12, "TBC"), "<", 10);

        Assert.Equal(NodeState.Warning, node.State);
        Assert.Equal(new object?[] { 5 }, Out(node).ToArray());
        Assert.Contains("2 of 4 items cannot be compared with '10'", Messages(node));
        Assert.Contains("item 1, 'abc'", Messages(node));
    }

    [Fact]
    public void NumbersReadAsTextAreStillComparedAsNumbers()
    {
        Assert.Equal(new object?[] { "5", " 7 " }, (List<object?>)Filter(L("5", "12", " 7 "), "<", 10)["matched"]);
    }

    [Fact]
    public void ARunawayRegexIsAFriendlyError()
    {
        var node = Run("List.FilterByValue", L(new string('a', 40) + "!"), "regex", "^(a+)+$");

        Assert.Equal(NodeState.Error, node.State);
        Assert.Contains("took longer than 2 seconds and was stopped", Messages(node));
        Assert.Contains("Simplify the pattern", Messages(node));
        Assert.DoesNotContain("Regex engine", Messages(node));
    }

    // ------------------------------------------------------------ COL-35 membership

    [Fact]
    public void In_KeepsTheItemsEqualToAnyValueOfAList_NotInKeepsTheOthers()
    {
        var levels = L("L01", "l02", "L03", "L04", null);

        var allowed = Filter(levels, "in", L("L01", "L02", "L03"));
        Assert.Equal(new object?[] { "L01", "l02", "L03" }, (List<object?>)allowed["matched"]);
        Assert.Equal(new object?[] { true, true, true, false, false }, (List<object?>)allowed["mask"]);

        var others = Filter(levels, "notIn", L("L01", "L02", "L03"));
        Assert.Equal(new object?[] { "L04", null }, (List<object?>)others["matched"]);

        var caseSensitive = Filter(levels, "in", L("L01", "L02"), ignoreCase: false);
        Assert.Equal(new object?[] { "L01" }, (List<object?>)caseSensitive["matched"]);
    }

    [Fact]
    public void In_AcceptsATextWithCommaOrSemicolonSeparatedValues_AndNumbersAgainstNumberText()
    {
        Assert.Equal(new object?[] { "L01", "L03" }, (List<object?>)Filter(L("L01", "L02", "L03"), "in", "L01, L03")["matched"]);
        Assert.Equal(new object?[] { "L02" }, (List<object?>)Filter(L("L01", "L02", "L03"), "in", "L02;")["matched"]);
        Assert.Equal(new object?[] { 2, "3" }, (List<object?>)Filter(L(1, 2, "3", "4"), "in", L("2", 3))["matched"]);
        Assert.Equal(new object?[] { 7 }, (List<object?>)Filter(L(7, 8), "in", 7)["matched"]);          // a single value is the only member
        Assert.Empty((List<object?>)Filter(L(1, 2), "in", null)["matched"]);                              // nothing is allowed
        Assert.Equal(2, ((List<object?>)Filter(L(1, 2), "notIn", null)["matched"]).Count);
    }

    [Fact]
    public void In_TestsTheKeysWhenTheyAreGiven_AndIsOfferedInTheTestDropdown()
    {
        var result = Filter(L("wall", "door", "slab"), "in", L("A", "B"), L("A", "C", "B"));
        Assert.Equal(new object?[] { "wall", "slab" }, (List<object?>)result["matched"]);

        var choices = Definition("List.FilterByValue").Inputs.Single(i => i.Name == "test").Choices!;
        Assert.Contains("in", choices);
        Assert.Contains("notIn", choices);
    }

    [Fact]
    public void In_RunsUnderTheEngine_WithAnyWiredList()
    {
        var node = Run("List.FilterByValue", L("a", "b", "c"), "in", L("b", "c"));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(new object?[] { "b", "c" }, Out(node).ToArray());
    }

    [Fact]
    public void TheOtherTestsStillWork_AsBefore()
    {
        Assert.Equal(new object?[] { "Wall A" }, (List<object?>)Filter(L("Wall A", "Door"), "contains", "wall")["matched"]);
        Assert.Equal(new object?[] { "A-1" }, (List<object?>)Filter(L("A-1", "B-1"), "matches", "A-*")["matched"]);
        Assert.Equal(new object?[] { 5 }, (List<object?>)Filter(L(5, 6), "==", 5)["matched"]);
        var bad = Assert.Throws<ArgumentException>(() => Filter(L(1), "around", 1));
        Assert.Contains("not a known test", bad.Message);
    }
}
