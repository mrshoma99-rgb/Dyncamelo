using System;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;
using Xunit;
using static CamelGraph.Nodes.Tests.NodeRun;

namespace CamelGraph.Nodes.Tests;

/// <summary>
/// COL-01: two lists with the same content are equal, and so are two dictionaries with the same entries (in any key order).
/// Every list node that compares values (unique, contains, index of, group, sets, tallies) and Equals share the rule.
/// </summary>
public class ListStructuralEqualityTests
{
    private static Dictionary<string, object?> D(params (string Key, object? Value)[] entries)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    // ------------------------------------------------------------ the shared rule

    [Fact]
    public void TwoListsWithTheSameItemsAreEqual_NumbersByValue_TextWithCase()
    {
        Assert.True(ValueComparison.AreEqual(L(1, 2), L(1.0, 2L)));
        Assert.True(ValueComparison.AreEqual(L("a", L(1, 2)), L("a", L(1, 2))));
        Assert.True(ValueComparison.AreEqual(L(), new object?[0]));
        Assert.False(ValueComparison.AreEqual(L(1, 2), L(2, 1)));        // order matters in a list
        Assert.False(ValueComparison.AreEqual(L(1, 2), L(1, 2, 3)));
        Assert.False(ValueComparison.AreEqual(L("a"), L("A")));          // text is case sensitive, like Equals
        Assert.False(ValueComparison.AreEqual(L(1), 1));
        Assert.False(ValueComparison.AreEqual(1, L(1)));
        Assert.False(ValueComparison.AreEqual(L(1), null));
    }

    [Fact]
    public void TwoDictionariesWithTheSameEntriesAreEqual_WhateverTheKeyOrder()
    {
        var a = D(("x", 1), ("y", L(1, 2)), ("z", D(("p", "q"), ("r", null))));
        var b = D(("z", D(("r", null), ("p", "q"))), ("y", L(1.0, 2.0)), ("x", 1L));

        Assert.True(ValueComparison.AreEqual(a, b));
        Assert.Equal(ValueComparison.GetValueHashCode(a), ValueComparison.GetValueHashCode(b));
        Assert.False(ValueComparison.AreEqual(a, D(("x", 1), ("y", L(1, 2)))));                         // fewer keys
        Assert.False(ValueComparison.AreEqual(D(("x", 1)), D(("x", 2))));                               // another value
        Assert.False(ValueComparison.AreEqual(D(("x", 1)), D(("y", 1))));                               // another key
        Assert.False(ValueComparison.AreEqual(D(("x", L(1, 2))), D(("x", L(2, 1)))));                   // list order inside still matters
        Assert.False(ValueComparison.AreEqual(D(("x", 1)), L(1)));
    }

    [Fact]
    public void TheHashAgreesWithEquality()
    {
        var pairs = new (object?, object?)[]
        {
            (L(1, 2, 3), L(1.0, 2.0, 3.0)),
            (L(L("a", 1), L("b", 2)), L(L("a", 1L), L("b", 2L))),
            (D(("a", 1), ("b", 2)), D(("b", 2), ("a", 1))),
            (L(D(("k", L(1))), null), L(D(("k", L(1.0))), null)),
        };

        foreach (var (left, right) in pairs)
        {
            Assert.True(ValueComparison.AreEqual(left, right));
            Assert.Equal(ValueComparison.GetValueHashCode(left), ValueComparison.GetValueHashCode(right));
        }
    }

    [Fact]
    public void ADeeplyNestedListIsNotFollowedForever()
    {
        object? Deep(int levels)
        {
            object? value = 1;
            for (var i = 0; i < levels; i++)
            {
                value = L(value);
            }

            return value;
        }

        // Well past any real data; must come back (false) instead of overflowing the stack of the host.
        var a = Deep(5000);
        var b = Deep(5000);
        Assert.False(ValueComparison.AreEqual(a, b));
        ValueComparison.GetValueHashCode(a);
        Assert.True(ValueComparison.AreEqual(Deep(20), Deep(20)));
    }

    [Fact]
    public void EqualsNodeComparesListsAndDictionariesByContent()
    {
        Assert.True(LogicNodes.EqualTo(L(1, 5), L(1, 5)));
        Assert.False(LogicNodes.EqualTo(L(1, 5), L(5, 1)));
        Assert.True(LogicNodes.EqualTo(D(("a", 1), ("b", 2)), D(("b", 2), ("a", 1))));
        Assert.False(LogicNodes.EqualTo(D(("a", 1)), D(("a", 2))));
    }

    // ------------------------------------------------------------ the list nodes

    [Fact]
    public void UniqueItems_RemovesRepeatedPairs()
    {
        var pairs = L(L("L1", "Wall"), L("L2", "Wall"), L("L1", "Wall"), L("L1", 5));

        var result = ListNodes.UniqueItems(pairs);

        Assert.Equal(3, result.Count);
        Assert.Equal(L("L1", "Wall"), result[0]);
        Assert.Equal(L("L2", "Wall"), result[1]);
        Assert.Equal(L("L1", 5), result[2]);
    }

    [Fact]
    public void UniqueItems_RemovesRepeatedDictionaries()
    {
        var items = L(D(("a", 1), ("b", 2)), D(("b", 2), ("a", 1)), D(("a", 1)));

        Assert.Equal(2, ListNodes.UniqueItems(items).Count);
    }

    [Fact]
    public void GroupByKey_GroupsByACompositeKey()
    {
        var items = L("w1", "w2", "d1", "w3");
        var keys = L(L("L1", "Wall"), L("L1", "Wall"), L("L1", "Door"), L("L2", "Wall"));

        var result = ListNodes.GroupByKey(items, keys);

        var groups = (List<object?>)result["groups"];
        Assert.Equal(3, groups.Count);
        Assert.Equal(L("w1", "w2"), groups[0]);
        Assert.Equal(L("d1"), groups[1]);
        Assert.Equal(L("w3"), groups[2]);
        Assert.Equal(3, ((List<object?>)result["uniqueKeys"]).Count);
    }

    [Fact]
    public void ContainsIndexOfLastIndexOfAndAllIndicesOf_FindAListItem()
    {
        var pairs = L(L(1, 2), L(3, 4), L(1, 2), "x");

        Assert.True(ListNodes.Contains(pairs, L(1.0, 2.0)));
        Assert.False(ListNodes.Contains(pairs, L(2, 1)));
        Assert.Equal(1, ListNodes.IndexOf(pairs, L(3, 4)));
        Assert.Equal(2, ListNodes.LastIndexOf(pairs, L(1, 2)));
        Assert.Equal(new[] { 0, 2 }, ListNodes.AllIndicesOf(pairs, L(1, 2)));
    }

    [Fact]
    public void SetNodes_CompareListItemsByContent()
    {
        var first = L(L(1, 2), L(3, 4), L(1, 2));
        var second = L(L(3, 4), L(5, 6));

        Assert.Equal(3, ListNodes.SetUnion(first, second).Count);
        Assert.Equal(new object?[] { L(3, 4) }, ListNodes.SetIntersection(first, second).ToArray());
        Assert.Equal(new object?[] { L(1, 2) }, ListNodes.SetDifference(first, second).ToArray());
    }

    [Fact]
    public void CountByDuplicatesAndMostCommon_CountEqualLists()
    {
        var pairs = L(L("a", 1), L("b", 2), L("a", 1), L("a", 1), L("b", 2), L("c", 3));

        var tally = ListStatsNodes.CountBy(pairs);
        Assert.Equal(3, ((List<object?>)tally["values"]).Count);
        Assert.Equal(new object?[] { 3, 2, 1 }, ((List<object?>)tally["counts"]).ToArray());

        var duplicates = ListStatsNodes.Duplicates(pairs);
        Assert.Equal(2, ((List<object?>)duplicates["duplicates"]).Count);

        var most = ListStatsNodes.MostCommon(pairs);
        Assert.Equal(L("a", 1), most["item"]);
        Assert.Equal(3, most["count"]);
    }

    [Fact]
    public void RunUnderTheEngine_UniqueItemsOfZippedPairs()
    {
        var node = Run("List.UniqueItems", L(L("x", 1), L("x", 1), L("y", 2)));

        Assert.Equal(NodeState.Executed, node.State);
        Assert.Equal(2, Out(node).Count);
    }
}
