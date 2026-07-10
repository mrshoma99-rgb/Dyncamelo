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

    [Fact]
    public void LastItem_ReturnsLastElement_ThrowsWhenEmpty()
    {
        Assert.Equal(3, ListNodes.LastItem(L(1, 2, 3)));
        Assert.Throws<InvalidOperationException>(() => ListNodes.LastItem(L()));
    }

    [Fact]
    public void Contains_UsesCoercingEquality()
    {
        Assert.True(ListNodes.Contains(L(1, 2, 3), 2.0));
        Assert.True(ListNodes.Contains(L("a", null), null));
        Assert.False(ListNodes.Contains(L(1, 2, 3), "2"));
        Assert.False(ListNodes.Contains(L(), 1));
    }

    [Fact]
    public void IndexOf_ReturnsFirstMatch_MinusOneWhenAbsent()
    {
        Assert.Equal(1, ListNodes.IndexOf(L("a", "b", "b"), "b"));
        Assert.Equal(0, ListNodes.IndexOf(L(2, 2.0), 2L));
        Assert.Equal(-1, ListNodes.IndexOf(L(1, 2), 9));
        Assert.Equal(1, ListNodes.IndexOf(L("x", null), null));
    }

    [Fact]
    public void Reverse_ReturnsNewReversedList_InputUntouched()
    {
        var input = L(1, 2, 3);
        var reversed = ListNodes.Reverse(input);
        Assert.Equal(new object?[] { 3, 2, 1 }, reversed);
        Assert.Equal(new object?[] { 1, 2, 3 }, input);
    }

    [Fact]
    public void AddItemToEnd_AppendsWithoutMutatingInput()
    {
        var input = L(1, 2);
        var result = ListNodes.AddItemToEnd(input, 3);
        Assert.Equal(new object?[] { 1, 2, 3 }, result);
        Assert.Equal(new object?[] { 1, 2 }, input);
    }

    [Fact]
    public void Join_ConcatenatesTwoLists()
    {
        Assert.Equal(new object?[] { 1, 2, "a" }, ListNodes.Join(L(1, 2), L("a")));
        Assert.Equal(new object?[] { 1 }, ListNodes.Join(L(1), L()));
        var ex = Assert.Throws<ArgumentNullException>(() => ListNodes.Join(L(1), null!));
        Assert.Contains("listB", ex.Message);
    }

    [Fact]
    public void RemoveItemAtIndex_RemovesElement_SupportsNegativeIndex()
    {
        var input = L("a", "b", "c");
        Assert.Equal(new object?[] { "a", "c" }, ListNodes.RemoveItemAtIndex(input, 1));
        Assert.Equal(new object?[] { "a", "b" }, ListNodes.RemoveItemAtIndex(input, -1));
        Assert.Equal(new object?[] { "a", "b", "c" }, input);
    }

    [Fact]
    public void RemoveItemAtIndex_OutOfRange_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ListNodes.RemoveItemAtIndex(L("a"), 5));
        Assert.Contains("5", ex.Message);
        Assert.Contains("1 element", ex.Message);
    }

    [Fact]
    public void GroupByKey_GroupsInFirstAppearanceOrder()
    {
        var result = ListNodes.GroupByKey(
            L("duct", "pipe", "tray", "elbow"),
            L("HVAC", "Plumbing", "HVAC", "Plumbing"));

        var uniqueKeys = Assert.IsAssignableFrom<IList<object?>>(result["uniqueKeys"]);
        Assert.Equal(new object?[] { "HVAC", "Plumbing" }, uniqueKeys);

        var groups = Assert.IsAssignableFrom<IList<object?>>(result["groups"]);
        Assert.Equal(new object?[] { "duct", "tray" }, (IList<object?>)groups[0]!);
        Assert.Equal(new object?[] { "pipe", "elbow" }, (IList<object?>)groups[1]!);
    }

    [Fact]
    public void GroupByKey_NumericKeysCoerce_AndNullKeysGroupTogether()
    {
        var result = ListNodes.GroupByKey(L("a", "b", "c", "d"), L(1, null, 1.0, null));

        var uniqueKeys = (IList<object?>)result["uniqueKeys"];
        Assert.Equal(2, uniqueKeys.Count);
        Assert.Equal(1, uniqueKeys[0]);
        Assert.Null(uniqueKeys[1]);

        var groups = (IList<object?>)result["groups"];
        Assert.Equal(new object?[] { "a", "c" }, (IList<object?>)groups[0]!);
        Assert.Equal(new object?[] { "b", "d" }, (IList<object?>)groups[1]!);
    }

    [Fact]
    public void GroupByKey_LengthMismatch_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ListNodes.GroupByKey(L(1, 2), L("k")));
        Assert.Contains("same length", ex.Message);
    }

    [Fact]
    public void SortByKey_SortsItemsAndKeysTogether()
    {
        var result = ListNodes.SortByKey(L("late", "early", "mid"), L(3, 1, 2));
        Assert.Equal(new object?[] { "early", "mid", "late" }, (IList<object?>)result["sorted"]);
        Assert.Equal(new object?[] { 1, 2, 3 }, (IList<object?>)result["sortedKeys"]);
    }

    [Fact]
    public void SortByKey_IsStable_AndDoesNotMutateInputs()
    {
        var items = L("first", "second", "third");
        var keys = L(1, 1, 0);
        var result = ListNodes.SortByKey(items, keys);
        Assert.Equal(new object?[] { "third", "first", "second" }, (IList<object?>)result["sorted"]);
        Assert.Equal(new object?[] { "first", "second", "third" }, items);
        Assert.Equal(new object?[] { 1, 1, 0 }, keys);
    }

    [Fact]
    public void SortByKey_LengthMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() => ListNodes.SortByKey(L(1), L(1, 2)));
    }
}
