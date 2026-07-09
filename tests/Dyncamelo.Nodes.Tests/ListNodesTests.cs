using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

public class ListNodesTests
{
    private static List<object?> L(params object?[] items) => new List<object?>(items);

    [Fact]
    public void GetItemAtIndex_ReturnsElement()
    {
        Assert.Equal("b", ListNodes.GetItemAtIndex(L("a", "b", "c"), 1));
    }

    [Fact]
    public void GetItemAtIndex_NegativeIndex_CountsFromEnd()
    {
        Assert.Equal("c", ListNodes.GetItemAtIndex(L("a", "b", "c"), -1));
        Assert.Equal("a", ListNodes.GetItemAtIndex(L("a", "b", "c"), -3));
    }

    [Fact]
    public void GetItemAtIndex_OutOfRange_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ListNodes.GetItemAtIndex(L("a"), 3));
        Assert.Contains("3", ex.Message);
        Assert.Contains("1 element", ex.Message);
    }

    [Fact]
    public void Count_ReturnsElementCount()
    {
        Assert.Equal(0, ListNodes.Count(L()));
        Assert.Equal(3, ListNodes.Count(L(1, 2, 3)));
    }

    [Fact]
    public void FirstItem_ReturnsFirstElement_ThrowsWhenEmpty()
    {
        Assert.Equal(1, ListNodes.FirstItem(L(1, 2)));
        Assert.Throws<InvalidOperationException>(() => ListNodes.FirstItem(L()));
    }

    [Fact]
    public void Flatten_CompletelyByDefault()
    {
        var nested = L(1, L(2, L(3, 4)), 5);
        Assert.Equal(new object?[] { 1, 2, 3, 4, 5 }, ListNodes.Flatten(nested));
    }

    [Fact]
    public void Flatten_ByOneLevel_KeepsDeeperNesting()
    {
        var nested = L(1, L(2, L(3)));
        var result = ListNodes.Flatten(nested, 1);
        Assert.Equal(1, result[0]);
        Assert.Equal(2, result[1]);
        var inner = Assert.IsAssignableFrom<IList<object?>>(result[2]);
        Assert.Equal(new object?[] { 3 }, inner);
    }

    [Fact]
    public void FilterByBoolMask_SplitsIntoInAndOut()
    {
        var result = ListNodes.FilterByBoolMask(L("a", "b", "c"), L(true, false, true));
        Assert.Equal(new object?[] { "a", "c" }, (IList<object?>)result["in"]);
        Assert.Equal(new object?[] { "b" }, (IList<object?>)result["out"]);
    }

    [Fact]
    public void FilterByBoolMask_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => ListNodes.FilterByBoolMask(L(1, 2), L(true)));
    }

    [Fact]
    public void Range_AscendingInclusive()
    {
        Assert.Equal(new[] { 0d, 2d, 4d, 6d }, ListNodes.Range(0, 6, 2));
    }

    [Fact]
    public void Range_EndIsIncludedDespiteFloatDrift()
    {
        var result = ListNodes.Range(0, 1, 0.1);
        Assert.Equal(11, result.Count);
        Assert.Equal(1d, result[result.Count - 1], 9);
    }

    [Fact]
    public void Range_DescendingWithNegativeStep()
    {
        Assert.Equal(new[] { 3d, 2d, 1d }, ListNodes.Range(3, 1, -1));
    }

    [Fact]
    public void Range_StepMovingAwayFromEnd_YieldsEmpty()
    {
        Assert.Empty(ListNodes.Range(5, 1, 1));
    }

    [Fact]
    public void Range_ZeroStep_Throws()
    {
        Assert.Throws<ArgumentException>(() => ListNodes.Range(0, 1, 0));
    }

    [Fact]
    public void Sort_OrdersNumbersAcrossBoxedTypes()
    {
        Assert.Equal(new object?[] { 1, 2.5, 3L }, ListNodes.Sort(L(3L, 1, 2.5)));
    }

    [Fact]
    public void Sort_OrdersStringsOrdinally_AndDoesNotMutateInput()
    {
        var input = L("pear", "apple", "fig");
        var sorted = ListNodes.Sort(input);
        Assert.Equal(new object?[] { "apple", "fig", "pear" }, sorted);
        Assert.Equal(new object?[] { "pear", "apple", "fig" }, input);
    }

    [Fact]
    public void Sort_MixedIncomparableTypes_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ListNodes.Sort(L(1, "a")).ToList());
        Assert.Contains("compare", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UniqueItems_RemovesDuplicates_PreservesOrder_NumbersByValue()
    {
        Assert.Equal(new object?[] { 1, "a", 2.5 }, ListNodes.UniqueItems(L(1, "a", 1.0, 2.5, "a")));
    }

    [Fact]
    public void UniqueItems_TreatsNullAsAValue()
    {
        Assert.Equal(new object?[] { null, 1 }, ListNodes.UniqueItems(L(null, 1, null)));
    }

    [Fact]
    public void NullList_ThrowsHelpfulMessage()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => ListNodes.Count(null!));
        Assert.Contains("List.Count", ex.Message);
    }
}
