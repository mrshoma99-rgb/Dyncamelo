using System;
using System.Collections.Generic;
using Xunit;

namespace Dyncamelo.Nodes.Tests;

/// <summary>
/// Pins the Dynamo-parity list wave (v0.31): indices, editing, sublists, set
/// operations, boolean aggregates, null replacement — and the null-tolerant
/// mask fix on List.FilterByBoolMask.
/// </summary>
public class ListDynamoParityTests
{
    private static List<object?> L(params object?[] items) => new List<object?>(items);

    // ------------------------------------------------------------- indices

    [Fact]
    public void AllIndicesOf_FindsEveryOccurrence_ByValueEquality()
    {
        Assert.Equal(new List<int> { 0, 2, 4 }, ListNodes.AllIndicesOf(L("a", "b", "a", "c", "a"), "a"));
        Assert.Equal(new List<int> { 1 }, ListNodes.AllIndicesOf(L(1.0, 2, 3.0), 2.0)); // int/double equality
        Assert.Empty(ListNodes.AllIndicesOf(L("a"), "z"));
    }

    [Fact]
    public void LastIndexOf_FindsTheLastOccurrence()
    {
        Assert.Equal(4, ListNodes.LastIndexOf(L("a", "b", "a", "c", "a"), "a"));
        Assert.Equal(-1, ListNodes.LastIndexOf(L("a"), "z"));
    }

    // ------------------------------------------------------------- editing

    [Fact]
    public void ReplaceNulls_PatchesEveryLevel_KeepingShape()
    {
        var patched = ListNodes.ReplaceNulls(L("a", null, L(1.0, null)), "n/a");
        Assert.Equal("a", patched[0]);
        Assert.Equal("n/a", patched[1]);
        Assert.Equal(new List<object?> { 1.0, "n/a" }, patched[2]);
    }

    [Fact]
    public void ReplaceItemAtIndex_SupportsNegativeIndexes_AndDoesNotMutateTheInput()
    {
        var source = L("a", "b", "c");
        var replaced = ListNodes.ReplaceItemAtIndex(source, -1, "z");
        Assert.Equal(new List<object?> { "a", "b", "z" }, replaced);
        Assert.Equal(new List<object?> { "a", "b", "c" }, source);
        Assert.Throws<ArgumentOutOfRangeException>(() => ListNodes.ReplaceItemAtIndex(source, 3, "z"));
    }

    [Fact]
    public void Insert_FrontMiddleEnd_AndNegative()
    {
        Assert.Equal(new List<object?> { "z", "a", "b" }, ListNodes.Insert(L("a", "b"), "z", 0));
        Assert.Equal(new List<object?> { "a", "b", "z" }, ListNodes.Insert(L("a", "b"), "z", 2));
        Assert.Equal(new List<object?> { "a", "z", "b" }, ListNodes.Insert(L("a", "b"), "z", -1));
    }

    [Fact]
    public void AddItemToFront_And_RestOfItems_AreHeadTailTwins()
    {
        Assert.Equal(new List<object?> { "z", "a" }, ListNodes.AddItemToFront(L("a"), "z"));
        Assert.Equal(new List<object?> { "b", "c" }, ListNodes.RestOfItems(L("a", "b", "c")));
    }

    // ------------------------------------------------------------ sublists

    [Fact]
    public void DropItems_And_TakeItems_NegativeMeansFromTheEnd()
    {
        Assert.Equal(new List<object?> { "c", "d" }, ListNodes.DropItems(L("a", "b", "c", "d"), 2));
        Assert.Equal(new List<object?> { "a", "b" }, ListNodes.DropItems(L("a", "b", "c", "d"), -2));
        Assert.Equal(new List<object?> { "a", "b" }, ListNodes.TakeItems(L("a", "b", "c", "d"), 2));
        Assert.Equal(new List<object?> { "c", "d" }, ListNodes.TakeItems(L("a", "b", "c", "d"), -2));
        Assert.Empty(ListNodes.DropItems(L("a"), 5));
        Assert.Equal(new List<object?> { "a" }, ListNodes.TakeItems(L("a"), 5));
    }

    [Fact]
    public void Slice_HonorsRangeAndStep_WithNegativeBounds()
    {
        var source = L(0.0, 1.0, 2.0, 3.0, 4.0, 5.0);
        Assert.Equal(new List<object?> { 1.0, 2.0, 3.0 }, ListNodes.Slice(source, 1, 4));
        Assert.Equal(new List<object?> { 0.0, 2.0, 4.0 }, ListNodes.Slice(source, 0, 6, 2));
        Assert.Equal(new List<object?> { 4.0 }, ListNodes.Slice(source, -2, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ListNodes.Slice(source, 0, 3, 0));
    }

    [Fact]
    public void Chop_SingleLength_AndCyclingLengths()
    {
        var even = ListNodes.Chop(L(1.0, 2.0, 3.0, 4.0, 5.0), L(2));
        Assert.Equal(3, even.Count);
        Assert.Equal(new List<object?> { 1.0, 2.0 }, even[0]);
        Assert.Equal(new List<object?> { 5.0 }, even[2]);

        var cycled = ListNodes.Chop(L(1.0, 2.0, 3.0, 4.0, 5.0, 6.0), L(1, 2));
        Assert.Equal(new List<object?> { 1.0 }, cycled[0]);
        Assert.Equal(new List<object?> { 2.0, 3.0 }, cycled[1]);
        Assert.Equal(new List<object?> { 4.0 }, cycled[2]);
        Assert.Equal(new List<object?> { 5.0, 6.0 }, cycled[3]);
    }

    [Fact]
    public void Transpose_SwapsRowsAndColumns_PaddingRaggedRowsWithNulls()
    {
        var transposed = ListNodes.Transpose(L(L("a1", "a2", "a3"), L("b1", "b2")));
        Assert.Equal(3, transposed.Count);
        Assert.Equal(new List<object?> { "a1", "b1" }, transposed[0]);
        Assert.Equal(new List<object?> { "a2", "b2" }, transposed[1]);
        Assert.Equal(new List<object?> { "a3", null }, transposed[2]);
    }

    [Fact]
    public void Cycle_And_OfRepeatedItem_Repeat()
    {
        Assert.Equal(new List<object?> { "a", "b", "a", "b" }, ListNodes.Cycle(L("a", "b"), 2));
        Assert.Empty(ListNodes.Cycle(L("a"), 0));
        Assert.Equal(new List<object?> { "x", "x", "x" }, ListNodes.OfRepeatedItem("x", 3));
    }

    [Fact]
    public void TakeEveryNthItem_WithOffset()
    {
        var source = L(1.0, 2.0, 3.0, 4.0, 5.0, 6.0);
        Assert.Equal(new List<object?> { 2.0, 4.0, 6.0 }, ListNodes.TakeEveryNthItem(source, 2));
        Assert.Equal(new List<object?> { 3.0, 5.0 }, ListNodes.TakeEveryNthItem(source, 2, 1));
    }

    [Fact]
    public void ShiftIndices_RotatesBothWays_AndWraps()
    {
        Assert.Equal(new List<object?> { "c", "a", "b" }, ListNodes.ShiftIndices(L("a", "b", "c"), 1));
        Assert.Equal(new List<object?> { "b", "c", "a" }, ListNodes.ShiftIndices(L("a", "b", "c"), -1));
        Assert.Equal(new List<object?> { "a", "b", "c" }, ListNodes.ShiftIndices(L("a", "b", "c"), 3));
    }

    // ---------------------------------------------------------- aggregates

    [Fact]
    public void MaximumItem_And_MinimumItem_IgnoreNulls()
    {
        Assert.Equal(9.0, ListNodes.MaximumItem(L(3.0, null, 9.0, 1.0)));
        Assert.Equal(1.0, ListNodes.MinimumItem(L(3.0, null, 9.0, 1.0)));
        Assert.Throws<InvalidOperationException>(() => ListNodes.MaximumItem(L((object?)null)));
    }

    [Fact]
    public void SetOperations_UseValueEquality_AndKeepFirstSeenOrder()
    {
        Assert.Equal(new List<object?> { "a", "b", "c" }, ListNodes.SetUnion(L("a", "b", "a"), L("b", "c")));
        Assert.Equal(new List<object?> { "b" }, ListNodes.SetIntersection(L("a", "b"), L("b", "c")));
        Assert.Equal(new List<object?> { "a" }, ListNodes.SetDifference(L("a", "b", "a"), L("b", "c")));
    }

    [Fact]
    public void BoolAggregates_TreatNullsAsNotTrue()
    {
        Assert.True(ListNodes.AllTrue(L(true, true)));
        Assert.False(ListNodes.AllTrue(L(true, null)));
        Assert.False(ListNodes.AllTrue(L()));
        Assert.True(ListNodes.AnyTrue(L(false, null, true)));
        Assert.False(ListNodes.AnyTrue(L(false, null)));

        var counts = ListNodes.CountTrue(L(true, false, null, true));
        Assert.Equal(2, counts["trueCount"]);
        Assert.Equal(2, counts["falseCount"]);
    }

    // -------------------------------------------------- mask null tolerance

    [Fact]
    public void FilterByBoolMask_NullMaskEntry_GoesToOut_InsteadOfFailing()
    {
        var result = ListNodes.FilterByBoolMask(L("a", "b", "c"), L(true, null, false));
        Assert.Equal(new List<object?> { "a" }, result["in"]);
        Assert.Equal(new List<object?> { "b", "c" }, result["out"]);
    }
}
